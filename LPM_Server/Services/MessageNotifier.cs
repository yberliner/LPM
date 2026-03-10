namespace LPM.Services;

/// <summary>
/// Singleton service that broadcasts message notifications across Blazor circuits.
/// When a message is sent, all subscribed components are notified instantly.
/// </summary>
public class MessageNotifier
{
    /// <summary>
    /// Fired when a new message is sent. Parameter is the recipient's PersonId.
    /// </summary>
    public event Action<int>? OnNewMessage;

    /// <summary>
    /// Call this after inserting a message to notify all subscribers.
    /// </summary>
    public void NotifyNewMessage(int recipientPersonId)
    {
        OnNewMessage?.Invoke(recipientPersonId);
    }
}
