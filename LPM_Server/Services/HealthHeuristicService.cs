using System.Collections.Concurrent;

namespace LPM.Services;

public sealed record HealthFinding(
    DateTime Utc,
    string Severity,    // "Info" | "Warn" | "Critical"
    string Code,        // stable id, e.g. "MEM_JUMP" — used for dedup
    string Title,
    string Detail);

/// <summary>
/// Background heuristic analyzer for the System Health tab. Polls MemoryWatchdogService
/// once a minute (~10s after the watchdog tick so we always see fresh data) and emits
/// human-readable findings when patterns of interest appear: sharp memory jumps, sustained
/// pressure, suspected leaks, circuit anomalies, cache near-cap.
///
/// Discipline (per spec — must not affect user traffic):
///   • All work runs on a background thread; never blocks UI.
///   • Whole tick wrapped in try/catch — exceptions logged & swallowed.
///   • Findings live in a 50-entry ring (in-memory only; reset on restart).
///   • Per-Code dedup: same finding suppressed for 30 min unless severity escalates.
///   • Skips ticks when fewer than 5 samples are available (need a baseline).
/// </summary>
public sealed class HealthHeuristicService : BackgroundService
{
    private readonly MemoryWatchdogService _mem;
    private static readonly TimeSpan _tickInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(70); // ~10s after watchdog
    private static readonly TimeSpan _dedupWindow  = TimeSpan.FromMinutes(30);

    private const int MaxFindings = 50;
    private readonly ConcurrentQueue<HealthFinding> _findings = new();
    private readonly Dictionary<string, (DateTime LastEmit, string Severity)> _lastEmit = new();
    private readonly object _emitLock = new();

    public HealthHeuristicService(MemoryWatchdogService mem) { _mem = mem; }

    public IReadOnlyList<HealthFinding> GetFindings() => _findings.ToArray();

    public void Clear()
    {
        lock (_emitLock)
        {
            while (_findings.TryDequeue(out _)) { }
            _lastEmit.Clear();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(_initialDelay, ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { Tick(); }
            catch (Exception ex) { Console.WriteLine($"[HealthHeuristic] tick failed: {ex.Message}"); }

            try { await Task.Delay(_tickInterval, ct); }
            catch (TaskCanceledException) { return; }
        }
    }

    private void Tick()
    {
        var snap = _mem.GetSnapshot();
        var samples = snap.Samples;
        if (samples.Count < 5) return; // not enough baseline

        var latest = samples[^1];
        var prev   = samples[^2];

        // ── 1. Memory critical (latest > 90%) ─────────────────────────────────
        if (latest.UsedPct > 90.0)
        {
            Emit("MEM_CRIT", "Critical", "Memory critical",
                $"System memory at {latest.UsedPct:F1}% ({latest.UsedMB:N0} / {latest.TotalMB:N0} MB).");
        }

        // ── 2. Sharp memory jump (≥10pp OR ≥200 MB vs prev) ───────────────────
        var pctDelta = latest.UsedPct - prev.UsedPct;
        var mbDelta  = latest.UsedMB  - prev.UsedMB;
        if (pctDelta >= 10.0 || mbDelta >= 200)
        {
            int activeDelta = latest.ActiveCircuits      - prev.ActiveCircuits;
            int discDelta   = latest.DisconnectedCircuits - prev.DisconnectedCircuits;
            var ctxParts = new List<string>();
            if (activeDelta != 0) ctxParts.Add($"active circuits {(activeDelta > 0 ? "+" : "")}{activeDelta}");
            if (discDelta   != 0) ctxParts.Add($"disconnected {(discDelta > 0 ? "+" : "")}{discDelta}");
            string ctx = ctxParts.Count > 0 ? $" Coincided with {string.Join(", ", ctxParts)}." : "";
            Emit("MEM_JUMP", "Warn", "Sharp memory jump",
                $"Memory rose {mbDelta:+#,0;-#,0;0} MB ({pctDelta:+0.0;-0.0;0.0}pp) in 1 min.{ctx}");
        }

        // ── 3. Sustained high memory (last 30 samples all > 80%) ──────────────
        if (samples.Count >= 30)
        {
            int hot = 0;
            for (int i = samples.Count - 30; i < samples.Count; i++)
                if (samples[i].UsedPct > 80.0) hot++;
            if (hot == 30)
            {
                Emit("MEM_SUSTAINED", "Warn", "Sustained high memory",
                    $"Memory has been > 80% for 30 minutes (currently {latest.UsedPct:F1}%).");
            }
        }

        // ── 4. Suspected leak (linear-fit slope over last 60 min > +50 MB/hr) ─
        if (samples.Count >= 60)
        {
            double slopeMbPerMin = LinearSlopeMb(samples, 60);
            double slopePerHr    = slopeMbPerMin * 60.0;
            if (slopePerHr > 50.0)
            {
                Emit("MEM_LEAK", "Warn", "Suspected memory leak",
                    $"Memory trending up at {slopePerHr:F0} MB/hr over the last hour with no recovery.");
            }
        }

        // ── 5. Circuit leak suspicion (Disconnected strictly grew over 10) ────
        if (samples.Count >= 10)
        {
            bool strictGrow = true;
            for (int i = samples.Count - 10; i < samples.Count - 1; i++)
                if (samples[i + 1].DisconnectedCircuits <= samples[i].DisconnectedCircuits)
                { strictGrow = false; break; }
            if (strictGrow && latest.DisconnectedCircuits >= 5)
            {
                Emit("CIRC_LEAK", "Info", "Disconnected circuits growing",
                    $"Disconnected count climbed every minute for 10 minutes (now {latest.DisconnectedCircuits}). " +
                    "Blazor retains these for 5 min after SignalR drop; if it keeps growing, GC may be lagging.");
            }
        }

        // ── 6. Disconnect surge (jumped ≥5 vs prev) ───────────────────────────
        if (latest.DisconnectedCircuits - prev.DisconnectedCircuits >= 5)
        {
            Emit("DISC_SURGE", "Warn", "Disconnect surge",
                $"Disconnected circuits +{latest.DisconnectedCircuits - prev.DisconnectedCircuits} " +
                $"in one minute (now {latest.DisconnectedCircuits}). Possible network event.");
        }

        // ── 7. Cache near cap (>95%) ──────────────────────────────────────────
        if (snap.CacheLimitBytes > 0 && latest.CacheBytes > snap.CacheLimitBytes * 0.95)
        {
            Emit("CACHE_FULL", "Info", "PDF cache near capacity",
                $"PDF cache at {latest.CacheBytes / 1024 / 1024:N0} MB " +
                $"of {snap.CacheLimitBytes / 1024 / 1024:N0} MB ({latest.CacheBytes * 100.0 / snap.CacheLimitBytes:F0}%).");
        }

        // ── 8. Recovery (was Warn/Critical, now back to baseline) ─────────────
        // If last MEM_CRIT or MEM_JUMP fired in the past 30 min and memory is now < 70%, mark recovered.
        var hadIssue = WasRecentEmit("MEM_CRIT") || WasRecentEmit("MEM_JUMP") || WasRecentEmit("MEM_SUSTAINED");
        if (hadIssue && latest.UsedPct < 70.0 && !WasRecentEmit("MEM_RECOVERED", TimeSpan.FromMinutes(60)))
        {
            Emit("MEM_RECOVERED", "Info", "Memory recovered",
                $"Memory back below baseline (now {latest.UsedPct:F1}%).");
        }
    }

    /// <summary>Slope of UsedMB over the last N samples, in MB per sample (≈ MB/min). Simple least-squares.</summary>
    private static double LinearSlopeMb(IReadOnlyList<MemorySample> samples, int lookback)
    {
        int n   = lookback;
        int off = samples.Count - lookback;
        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;
        for (int i = 0; i < n; i++)
        {
            double x = i;
            double y = samples[off + i].UsedMB;
            sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;
        }
        double denom = n * sumX2 - sumX * sumX;
        if (Math.Abs(denom) < 1e-9) return 0;
        return (n * sumXY - sumX * sumY) / denom;
    }

    private void Emit(string code, string severity, string title, string detail)
    {
        lock (_emitLock)
        {
            // Dedup: skip if same code emitted recently AND severity didn't escalate.
            if (_lastEmit.TryGetValue(code, out var last))
            {
                bool withinWindow = DateTime.UtcNow - last.LastEmit < _dedupWindow;
                bool escalated = SeverityRank(severity) > SeverityRank(last.Severity);
                if (withinWindow && !escalated) return;
            }
            _lastEmit[code] = (DateTime.UtcNow, severity);
        }

        _findings.Enqueue(new HealthFinding(DateTime.UtcNow, severity, code, title, detail));
        while (_findings.Count > MaxFindings) _findings.TryDequeue(out _);
    }

    private bool WasRecentEmit(string code, TimeSpan? window = null)
    {
        var w = window ?? _dedupWindow;
        lock (_emitLock)
        {
            return _lastEmit.TryGetValue(code, out var last)
                && DateTime.UtcNow - last.LastEmit < w;
        }
    }

    private static int SeverityRank(string s) => s switch
    {
        "Critical" => 3,
        "Warn"     => 2,
        "Info"     => 1,
        _          => 0,
    };
}
