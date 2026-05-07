using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace LPM.Services;

/// <summary>
/// One sample of memory state captured every ~5 minutes. UsedPct is system memory
/// pressure (Linux: from /proc/meminfo; Windows dev: process working set / GC total).
/// CacheBytes is the IMemoryCache (PDF cache) current estimated size in bytes.
/// ActiveCircuits / DisconnectedCircuits are the Blazor circuit counts at sample time —
/// overlaid on the memory history chart so the user can correlate "more circuits → more
/// memory used".
/// </summary>
public sealed record MemorySample(
    DateTime Utc,
    double UsedPct,
    long UsedMB,
    long TotalMB,
    long CacheBytes,
    int CacheEntries,
    int ActiveCircuits,
    int DisconnectedCircuits);

/// <summary>Snapshot returned to the UI — copy-on-read, safe to enumerate.</summary>
public sealed record MemorySnapshot(
    DateTime ProcessStartedAt,
    IReadOnlyList<MemorySample> Samples,
    long CacheLimitBytes);

/// <summary>
/// Background watchdog that polls system-wide memory pressure every 5 minutes and
/// clears the IMemoryCache (PDF bytes) when "used" exceeds <see cref="THRESHOLD_PCT_USED"/>%.
///
/// The PDF cache is a read-through optimisation — the source of truth is always the
/// file on disk. Clearing it is safe under concurrent use:
///   • An in-flight reader keeps its local byte[] (the cache eviction can't reach into it).
///   • An in-flight Set() may have its entry immediately evicted; the next read just re-fetches.
///   • A baked PDF is written to disk BEFORE StoreInCache() so the file is always correct.
/// The only consequence of clearing is a brief perf dip while the cache rewarms from disk —
/// which is exactly what you want when the box is short on RAM anyway.
///
/// On non-Linux environments (e.g. Windows dev), /proc/meminfo isn't present and the watchdog
/// silently no-ops — safe to leave registered.
/// </summary>
public sealed class MemoryWatchdogService : BackgroundService
{
    private const double THRESHOLD_PCT_USED = 85.0;
    private static readonly TimeSpan _interval     = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan _initialDelay = TimeSpan.FromMinutes(2);

    private readonly IMemoryCache _cache;
    private readonly CircuitTrackingService _circuits;
    private readonly long _cacheLimitBytes;

    // Tracked across wake-ups so we can compute LPM process CPU% as a delta.
    private TimeSpan _prevCpuTime;
    private DateTime _prevWallUtc;
    private bool _haveCpuBaseline;

    // Throttles the routine status WriteLine to every 10 ticks (= 10 min). Sampling for the
    // chart and pressure-threshold checks still happen every tick.
    private int _logTickCounter;

    // Sample ring — capped so memory stays small even on long uptimes.
    // 7 days × 60 samples/hour × 24h = 10,080 entries → ~500 KB.
    private const int MaxSamples = 10080;
    private readonly ConcurrentQueue<MemorySample> _samples = new();

    public MemoryWatchdogService(IMemoryCache cache, CircuitTrackingService circuits)
    {
        _cache    = cache;
        _circuits = circuits;
        // Mirror the configured SizeLimit so the UI can show "X / Y MB".
        // SizeLimit isn't surfaced via IMemoryCache, so we hardcode the same
        // value as Program.cs's AddMemoryCache(o => o.SizeLimit = ...).
        _cacheLimitBytes = 200L * 1024 * 1024;
    }

    /// <summary>Process start time — stable for the lifetime of this app instance.</summary>
    public DateTime ProcessStartedAt => Process.GetCurrentProcess().StartTime;

    /// <summary>Returns a copy of all samples plus metadata. Safe to call from any thread.</summary>
    public MemorySnapshot GetSnapshot()
    {
        // ToArray on ConcurrentQueue is atomic.
        var arr = _samples.ToArray();
        return new MemorySnapshot(ProcessStartedAt, arr, _cacheLimitBytes);
    }

    /// <summary>Live cache stats (current count + size). For the always-up-to-date card on System Health.</summary>
    public (long CurrentBytes, long Limit, int EntryCount, long Hits, long Misses) GetCacheStats()
    {
        if (_cache is MemoryCache mc)
        {
            var s = mc.GetCurrentStatistics();
            if (s != null)
                return (s.CurrentEstimatedSize ?? 0, _cacheLimitBytes, (int)s.CurrentEntryCount, s.TotalHits, s.TotalMisses);
        }
        return (0, _cacheLimitBytes, 0, 0, 0);
    }

    private void RecordSample(double usedPct, long usedMB, long totalMB)
    {
        var (cacheBytes, _, cacheEntries, _, _) = GetCacheStats();
        int active   = _circuits.ActiveCount;
        int disconn  = _circuits.DisconnectedCount;
        _samples.Enqueue(new MemorySample(DateTime.UtcNow, usedPct, usedMB, totalMB, cacheBytes, cacheEntries, active, disconn));
        // Trim oldest if over cap. Lock-free; one producer (Check) so the loop terminates fast.
        while (_samples.Count > MaxSamples) _samples.TryDequeue(out _);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Don't fire during JIT-heavy startup — memory is naturally inflated then.
        try { await Task.Delay(_initialDelay, ct); }
        catch (TaskCanceledException) { return; }

        while (!ct.IsCancellationRequested)
        {
            try { Check(); }
            catch (Exception ex) { Console.WriteLine($"[MemWatch] check failed: {ex.Message}"); }

            try { await Task.Delay(_interval, ct); }
            catch (TaskCanceledException) { return; }
        }
    }

    private void Check()
    {
        _logTickCounter++;
        // Routine status WriteLine fires on tick 1, 11, 21, … (= every 10 min). Pressure
        // warnings and sample recording still run every tick, regardless of this flag.
        bool logRoutine = _logTickCounter % 10 == 1;
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // ── CPU% for the LPM process: delta(CPU time) / delta(wall) / cpuCount ──
        var proc       = Process.GetCurrentProcess();
        var nowCpu     = proc.TotalProcessorTime;
        var nowWallUtc = DateTime.UtcNow;
        string lpmCpuStr;
        if (_haveCpuBaseline)
        {
            double deltaCpuMs  = (nowCpu - _prevCpuTime).TotalMilliseconds;
            double deltaWallMs = (nowWallUtc - _prevWallUtc).TotalMilliseconds;
            int    cores       = Math.Max(1, Environment.ProcessorCount);
            double cpuPct      = deltaWallMs > 0 ? (deltaCpuMs / (deltaWallMs * cores)) * 100.0 : 0;
            lpmCpuStr          = $"{cpuPct:F1}%";
        }
        else
        {
            lpmCpuStr = "(baseline)";
        }
        _prevCpuTime    = nowCpu;
        _prevWallUtc    = nowWallUtc;
        _haveCpuBaseline = true;

        // ── Process memory + thread count ──
        long procRssMB    = proc.WorkingSet64 / 1024 / 1024;
        long procPrivMB   = proc.PrivateMemorySize64 / 1024 / 1024;
        long gcHeapMB     = GC.GetTotalMemory(false) / 1024 / 1024;
        int  threadCount  = proc.Threads.Count;

        // ── System memory + load average (Linux only) ──
        bool gotMem  = TryReadMemInfoLinux(out long totalKB, out long availKB);
        bool gotLoad = TryReadLoadAvgLinux(out double load1, out double load5, out double load15);

        // ── Disk usage of the volume holding the app's working dir ──
        string diskStr = ReadDiskUsage();

        if (gotMem)
        {
            double pctUsed  = (totalKB - availKB) * 100.0 / totalKB;
            long   availMB  = availKB / 1024;
            long   totalMB  = totalKB / 1024;
            long   usedMB   = totalMB - availMB;
            string loadStr  = gotLoad ? $"{load1:F2}/{load5:F2}/{load15:F2}" : "n/a";
            int    cores    = Environment.ProcessorCount;

            RecordSample(pctUsed, usedMB, totalMB);

            if (logRoutine)
            {
                Console.WriteLine(
                    $"[MemWatch] {stamp} | sysMem used={pctUsed:F1}% (avail={availMB}MB/total={totalMB}MB) " +
                    $"| lpmRSS={procRssMB}MB priv={procPrivMB}MB gcHeap={gcHeapMB}MB " +
                    $"| lpmCpu={lpmCpuStr} sysLoad1m/5m/15m={loadStr} (cores={cores}) " +
                    $"| threads={threadCount} " +
                    $"| disk={diskStr}");
            }

            if (pctUsed > THRESHOLD_PCT_USED)
            {
                Console.WriteLine($"[MemWatch] {stamp} WARNING — sysMem used {pctUsed:F1}% > {THRESHOLD_PCT_USED}% — clearing IMemoryCache");
                // Compact(1.0) evicts 100% by size; only available on the concrete MemoryCache type.
                if (_cache is MemoryCache mc) mc.Compact(1.0);
                Console.WriteLine($"[MemWatch] {stamp} IMemoryCache cleared");
            }
        }
        else
        {
            // Non-Linux dev box — record using process working set as a stand-in so the
            // history chart still has something to show on Windows/macOS development.
            try
            {
                var gcInfo  = GC.GetGCMemoryInfo();
                long totalB = gcInfo.TotalAvailableMemoryBytes;
                long usedB  = proc.WorkingSet64;
                long totalMB = Math.Max(1, totalB / (1024 * 1024));
                long usedMB  = usedB / (1024 * 1024);
                double pct   = totalB > 0 ? (double)usedB / totalB * 100 : 0;
                RecordSample(pct, usedMB, totalMB);
            }
            catch { /* best-effort on dev */ }

            if (logRoutine)
            {
                Console.WriteLine(
                    $"[MemWatch] {stamp} | sysMem n/a (non-Linux) " +
                    $"| lpmRSS={procRssMB}MB priv={procPrivMB}MB gcHeap={gcHeapMB}MB " +
                    $"| lpmCpu={lpmCpuStr} (cores={Environment.ProcessorCount}) " +
                    $"| threads={threadCount} " +
                    $"| disk={diskStr}");
            }
        }
    }

    /// <summary>Disk usage of the drive containing the current working directory ("/" on Linux, e.g. "C:\" on Windows).</summary>
    private static string ReadDiskUsage()
    {
        try
        {
            var drive   = new DriveInfo(Path.GetPathRoot(Directory.GetCurrentDirectory()) ?? "/");
            long usedB  = drive.TotalSize - drive.AvailableFreeSpace;
            long totalB = drive.TotalSize;
            long availB = drive.AvailableFreeSpace;
            double pct  = totalB > 0 ? usedB * 100.0 / totalB : 0;
            return $"{pct:F1}% (avail={FormatGB(availB)}/total={FormatGB(totalB)} mount={drive.Name})";
        }
        catch (Exception ex)
        {
            return $"n/a ({ex.GetType().Name})";
        }
    }

    private static string FormatGB(long bytes)
    {
        double gb = bytes / 1024.0 / 1024.0 / 1024.0;
        return gb >= 1.0 ? $"{gb:F1}GB" : $"{bytes / 1024 / 1024}MB";
    }

    /// <summary>Read MemTotal + MemAvailable (KB) from /proc/meminfo. Returns false on non-Linux or parse error.</summary>
    private static bool TryReadMemInfoLinux(out long totalKB, out long availKB)
    {
        totalKB = 0; availKB = 0;
        try
        {
            if (!File.Exists("/proc/meminfo")) return false;
            foreach (var line in File.ReadAllLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) long.TryParse(parts[1], out totalKB);
                }
                else if (line.StartsWith("MemAvailable:"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) long.TryParse(parts[1], out availKB);
                }
                if (totalKB > 0 && availKB > 0) return true;
            }
            return totalKB > 0 && availKB > 0;
        }
        catch { return false; }
    }

    /// <summary>Read system load averages (1, 5, 15 minute) from /proc/loadavg. Returns false on non-Linux or parse error.</summary>
    private static bool TryReadLoadAvgLinux(out double load1, out double load5, out double load15)
    {
        load1 = load5 = load15 = 0;
        try
        {
            if (!File.Exists("/proc/loadavg")) return false;
            var parts = File.ReadAllText("/proc/loadavg").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            return double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out load1)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out load5)
                && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out load15);
        }
        catch { return false; }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Deep memory diagnostics — for the System Health "Memory Detail" card.
    // The watchdog isn't strictly the right home for these (they're not periodic
    // samples) but it's the existing memory-related singleton so callers find it.
    // ─────────────────────────────────────────────────────────────────────────────

    public sealed record GenInfo(string Name, long Size, long Fragmentation);
    public sealed record DeepMemoryReport(
        // Process
        long RssBytes, long PrivateBytes, long VirtualBytes, long PagedBytes,
        long GcHeapBytes,
        // GC heap detail
        long TotalCommittedBytes, long FragmentedBytes, long HeapSizeBytes,
        long HighMemoryLoadThreshold, long MemoryLoadBytes,
        IReadOnlyList<GenInfo> Generations,
        int GcCountGen0, int GcCountGen1, int GcCountGen2,
        // Threads
        int ThreadCount, int ThreadPoolThreadCount, long ThreadPoolPendingWorkItems, long ThreadPoolCompletedWorkItems,
        // File handles (Linux only; -1 if unavailable)
        int FileHandleCount);

    /// <summary>Snapshot of detailed process / GC / thread state. Cheap; safe to call every few seconds.</summary>
    public DeepMemoryReport GetDeepStats()
    {
        var proc = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();

        var gens = new List<GenInfo>();
        for (int g = 0; g < info.GenerationInfo.Length; g++)
        {
            var gi = info.GenerationInfo[g];
            string name = g switch
            {
                0 => "Gen 0",
                1 => "Gen 1",
                2 => "Gen 2",
                3 => "LOH",   // Large Object Heap
                4 => "POH",   // Pinned Object Heap
                _ => $"Gen {g}",
            };
            gens.Add(new GenInfo(name, gi.SizeAfterBytes, gi.FragmentationAfterBytes));
        }

        int fileHandles = -1;
        try
        {
            if (Directory.Exists("/proc/self/fd"))
                fileHandles = Directory.GetFileSystemEntries("/proc/self/fd").Length;
        }
        catch { /* best-effort */ }

        return new DeepMemoryReport(
            RssBytes:                     proc.WorkingSet64,
            PrivateBytes:                 proc.PrivateMemorySize64,
            VirtualBytes:                 proc.VirtualMemorySize64,
            PagedBytes:                   proc.PagedMemorySize64,
            GcHeapBytes:                  GC.GetTotalMemory(false),
            TotalCommittedBytes:          info.TotalCommittedBytes,
            FragmentedBytes:              info.FragmentedBytes,
            HeapSizeBytes:                info.HeapSizeBytes,
            HighMemoryLoadThreshold:      info.HighMemoryLoadThresholdBytes,
            MemoryLoadBytes:              info.MemoryLoadBytes,
            Generations:                  gens,
            GcCountGen0:                  GC.CollectionCount(0),
            GcCountGen1:                  GC.CollectionCount(1),
            GcCountGen2:                  GC.CollectionCount(2),
            ThreadCount:                  proc.Threads.Count,
            ThreadPoolThreadCount:        ThreadPool.ThreadCount,
            ThreadPoolPendingWorkItems:   ThreadPool.PendingWorkItemCount,
            ThreadPoolCompletedWorkItems: ThreadPool.CompletedWorkItemCount,
            FileHandleCount:              fileHandles);
    }

    public sealed record ForceGcResult(
        long BeforeRss, long AfterRss,
        long BeforeHeap, long AfterHeap,
        long BeforeLoh, long AfterLoh,
        long BeforeFragmented, long AfterFragmented,
        long DurationMs);

    /// <summary>
    /// Run a blocking, compacting Gen-2 GC with one-time LOH compaction. Logs and returns
    /// before/after RSS + heap sizes so the caller can show the delta. On a 1.2 GB heap this
    /// blocks the calling thread for ~1–3 seconds.
    /// </summary>
    public ForceGcResult ForceCompactingGc()
    {
        var sw       = System.Diagnostics.Stopwatch.StartNew();
        var proc     = Process.GetCurrentProcess();
        long beforeRss   = proc.WorkingSet64;
        long beforeHeap  = GC.GetTotalMemory(false);
        var beforeInfo   = GC.GetGCMemoryInfo();
        long beforeLoh   = beforeInfo.GenerationInfo.Length > 3 ? beforeInfo.GenerationInfo[3].SizeAfterBytes : 0;
        long beforeFrag  = beforeInfo.FragmentedBytes;

        Console.WriteLine($"[MemDebug] ForceGC: starting (RSS={beforeRss/1024/1024}MB heap={beforeHeap/1024/1024}MB LOH={beforeLoh/1024/1024}MB frag={beforeFrag/1024/1024}MB)");

        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

        proc.Refresh();
        long afterRss  = proc.WorkingSet64;
        long afterHeap = GC.GetTotalMemory(false);
        var afterInfo  = GC.GetGCMemoryInfo();
        long afterLoh  = afterInfo.GenerationInfo.Length > 3 ? afterInfo.GenerationInfo[3].SizeAfterBytes : 0;
        long afterFrag = afterInfo.FragmentedBytes;

        sw.Stop();
        Console.WriteLine($"[MemDebug] ForceGC: done in {sw.ElapsedMilliseconds}ms — " +
            $"RSS {beforeRss/1024/1024}→{afterRss/1024/1024}MB " +
            $"heap {beforeHeap/1024/1024}→{afterHeap/1024/1024}MB " +
            $"LOH {beforeLoh/1024/1024}→{afterLoh/1024/1024}MB " +
            $"frag {beforeFrag/1024/1024}→{afterFrag/1024/1024}MB");

        return new ForceGcResult(beforeRss, afterRss, beforeHeap, afterHeap, beforeLoh, afterLoh, beforeFrag, afterFrag, sw.ElapsedMilliseconds);
    }

    public sealed record TypeRow(string TypeName, long TotalSize, long ObjectCount);
    public sealed record HeapSnapshotResult(
        bool Ok, string? Error,
        long TotalObjectsScanned, long TotalSizeBytes,
        IReadOnlyList<TypeRow> TopTypes,
        long DurationMs);

    /// <summary>
    /// Walk the live managed heap with ClrMD and return the top <paramref name="topN"/> types
    /// by total retained bytes. Uses CreateSnapshotAndAttach for a consistent view.
    /// Expensive: takes 5–30s on a multi-GB heap and itself uses memory (~50–200 MB) to run.
    /// </summary>
    public Task<HeapSnapshotResult> GetTopTypesAsync(int topN = 25)
    {
        return Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                int pid = Environment.ProcessId;
                using var dataTarget = Microsoft.Diagnostics.Runtime.DataTarget.CreateSnapshotAndAttach(pid);
                var clrInfo = dataTarget.ClrVersions.FirstOrDefault();
                if (clrInfo == null)
                    return new HeapSnapshotResult(false, "No CLR found in process snapshot.", 0, 0, [], sw.ElapsedMilliseconds);

                using var runtime = clrInfo.CreateRuntime();
                var heap = runtime.Heap;
                if (!heap.CanWalkHeap)
                    return new HeapSnapshotResult(false, "Heap is not walkable (GC may be in progress).", 0, 0, [], sw.ElapsedMilliseconds);

                var byType = new Dictionary<string, (long size, long count)>(StringComparer.Ordinal);
                long total = 0, totalObjs = 0;
                foreach (var obj in heap.EnumerateObjects())
                {
                    if (!obj.IsValid) continue;
                    var name = obj.Type?.Name ?? "(unknown)";
                    long sz   = (long)obj.Size;
                    if (byType.TryGetValue(name, out var t))
                        byType[name] = (t.size + sz, t.count + 1);
                    else
                        byType[name] = (sz, 1);
                    total     += sz;
                    totalObjs++;
                }

                var top = byType
                    .OrderByDescending(kv => kv.Value.size)
                    .Take(topN)
                    .Select(kv => new TypeRow(kv.Key, kv.Value.size, kv.Value.count))
                    .ToList();

                sw.Stop();
                Console.WriteLine($"[MemDebug] HeapSnapshot: {totalObjs:N0} objects, {total / 1024 / 1024} MB total, top type: {top.FirstOrDefault()?.TypeName} ({top.FirstOrDefault()?.TotalSize / 1024 / 1024} MB) in {sw.ElapsedMilliseconds}ms");
                return new HeapSnapshotResult(true, null, totalObjs, total, top, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                Console.WriteLine($"[MemDebug] HeapSnapshot FAILED: {ex.GetType().Name}: {ex.Message}");
                return new HeapSnapshotResult(false, $"{ex.GetType().Name}: {ex.Message}", 0, 0, [], sw.ElapsedMilliseconds);
            }
        });
    }
}
