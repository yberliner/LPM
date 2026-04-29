using System.Collections.Concurrent;

namespace LPM.Services;

/// <summary>
/// Per-circuit lifecycle data. A circuit is a single user session in Blazor Server —
/// each browser tab/window opens one. Each circuit holds a server-side render tree,
/// JS-interop refs, and component state — so a leaked (un-disposed) circuit pins
/// significant memory.
/// </summary>
public sealed class CircuitInfo
{
    public required string CircuitId { get; init; }
    public required string Username { get; init; }
    public required DateTime OpenedAtUtc { get; init; }
    public DateTime LastConnectedAtUtc { get; set; }
    public DateTime? DisconnectedAtUtc { get; set; }
    /// <summary>True when the underlying SignalR connection is currently alive.</summary>
    public bool IsConnected { get; set; }
}

public sealed class CircuitTrackingService
{
    private readonly ConcurrentDictionary<string, CircuitInfo> _circuits = new();

    public void Open(string circuitId, string username)
    {
        var now = DateTime.UtcNow;
        _circuits[circuitId] = new CircuitInfo
        {
            CircuitId          = circuitId,
            Username           = username ?? "",
            OpenedAtUtc        = now,
            LastConnectedAtUtc = now,
            IsConnected        = true,
        };
        Console.WriteLine($"[Circuits] open {ShortId(circuitId)} user='{username}' (active={_circuits.Count})");
    }

    /// <summary>When true, every circuit close triggers a blocking full GC and logs how much
    /// heap freed. Useful for "how big was this circuit" diagnostics, costs ~100–500ms blocking
    /// per close on the thread-pool thread that handles the close event. Flip to false if the
    /// extra GC churn becomes a problem.</summary>
    public static bool LogCloseHeapDelta = true;

    public void Close(string circuitId)
    {
        if (!_circuits.TryRemove(circuitId, out var info))
            return;

        var lifetime = DateTime.UtcNow - info.OpenedAtUtc;
        var userTag  = string.IsNullOrEmpty(info.Username) ? "(anon)" : info.Username;

        if (LogCloseHeapDelta)
        {
            // GC.GetTotalMemory(true) forces a blocking full collection and returns the
            // post-GC managed-heap size — exactly the "did this circuit's memory get freed"
            // signal we want. RSS won't drop immediately (the runtime keeps committed pages),
            // but the managed-heap delta tells us whether the closure was actually GC-rooted
            // until now.
            long beforeBytes = GC.GetTotalMemory(forceFullCollection: false);
            long afterBytes  = GC.GetTotalMemory(forceFullCollection: true);
            long deltaBytes  = beforeBytes - afterBytes;
            Console.WriteLine(
                $"[Circuits] close {ShortId(circuitId)} user='{userTag}' lifetime={FormatLifetime(lifetime)} " +
                $"heap={beforeBytes / 1024 / 1024}→{afterBytes / 1024 / 1024} MB delta={deltaBytes / 1024 / 1024} MB " +
                $"(active={_circuits.Count})");
        }
        else
        {
            Console.WriteLine($"[Circuits] close {ShortId(circuitId)} user='{userTag}' lifetime={FormatLifetime(lifetime)} (active={_circuits.Count})");
        }
    }

    private static string FormatLifetime(TimeSpan ts)
    {
        if (ts.TotalDays    >= 1) return $"{(int)ts.TotalDays}d {ts.Hours}h";
        if (ts.TotalHours   >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalSeconds}s";
    }

    public void ConnectionDown(string circuitId)
    {
        if (_circuits.TryGetValue(circuitId, out var c))
        {
            c.IsConnected      = false;
            c.DisconnectedAtUtc = DateTime.UtcNow;
            Console.WriteLine($"[Circuits] down {ShortId(circuitId)} (active={ActiveCount} disconnected={DisconnectedCount})");
        }
    }

    public void ConnectionUp(string circuitId)
    {
        if (_circuits.TryGetValue(circuitId, out var c))
        {
            c.IsConnected         = true;
            c.LastConnectedAtUtc  = DateTime.UtcNow;
            c.DisconnectedAtUtc   = null;
            Console.WriteLine($"[Circuits] up {ShortId(circuitId)} (active={ActiveCount})");
        }
    }

    /// <summary>Total tracked circuits — connected + disconnected-but-not-yet-disposed.</summary>
    public int Total            => _circuits.Count;
    public int ActiveCount      => _circuits.Values.Count(c => c.IsConnected);
    public int DisconnectedCount => _circuits.Values.Count(c => !c.IsConnected);

    public CircuitInfo? Oldest()
    {
        CircuitInfo? oldest = null;
        foreach (var c in _circuits.Values)
            if (oldest == null || c.OpenedAtUtc < oldest.OpenedAtUtc) oldest = c;
        return oldest;
    }

    /// <summary>Snapshot of all tracked circuits, copied for safe enumeration.</summary>
    public IReadOnlyList<CircuitInfo> Snapshot() => _circuits.Values.ToArray();

    private static string ShortId(string id) =>
        id.Length > 8 ? id[..8] : id;
}
