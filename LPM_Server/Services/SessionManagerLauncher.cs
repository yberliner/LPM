namespace LPM.Services;

public class SessionManagerLauncher
{
    public event Action<int>? OnOpenRequested;

    public void RequestOpen(int sessionId) => OnOpenRequested?.Invoke(sessionId);
}
