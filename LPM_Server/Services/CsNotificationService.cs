using System.Collections.Concurrent;

namespace LPM.Services;

/// <summary>
/// Singleton service that notifies connected Home.razor instances
/// when a CS review is completed, so auditors with permission for
/// that PC get an immediate screen refresh.
/// </summary>
public class CsNotificationService
{
    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = new();

    public record Subscriber(int UserId, Func<int, Task> OnCsDone);

    /// <summary>Register a Home.razor circuit. Returns a key for unsubscribing.</summary>
    public string Subscribe(int userId, Func<int, Task> onCsDone)
    {
        var key = Guid.NewGuid().ToString("N");
        _subscribers[key] = new Subscriber(userId, onCsDone);
        return key;
    }

    public void Unsubscribe(string key)
    {
        _subscribers.TryRemove(key, out _);
    }

    /// <summary>
    /// Called when a CS review is completed. Notifies all subscribers
    /// whose userId has approved permission for the given pcId.
    /// </summary>
    public async Task NotifyCsDone(int pcId, Func<int, int, bool> hasPermission)
    {
        var tasks = new List<Task>();
        foreach (var kvp in _subscribers)
        {
            var sub = kvp.Value;
            try
            {
                if (hasPermission(sub.UserId, pcId))
                    tasks.Add(SafeInvoke(kvp.Key, sub, pcId));
            }
            catch
            {
                // Circuit may be dead — remove it
                _subscribers.TryRemove(kvp.Key, out _);
            }
        }

        await Task.WhenAll(tasks);
    }

    private async Task SafeInvoke(string key, Subscriber sub, int pcId)
    {
        try { await sub.OnCsDone(pcId); }
        catch
        {
            // Callback failed (dead circuit) — auto-unsubscribe
            _subscribers.TryRemove(key, out _);
        }
    }
}
