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

    public void Close(string circuitId)
    {
        if (_circuits.TryRemove(circuitId, out _))
            Console.WriteLine($"[Circuits] close {ShortId(circuitId)} (active={_circuits.Count})");
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
