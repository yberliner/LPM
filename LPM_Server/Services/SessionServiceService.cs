using Microsoft.AspNetCore.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System;

public class SessionService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public SessionService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ISession? SafeSession
    {
        get
        {
            try
            {
                var ctx = _httpContextAccessor?.HttpContext;
                // After the initial HTTP request, Blazor Server runs over SignalR and
                // Response.HasStarted is true — session writes are not possible then.
                if (ctx == null || ctx.Response.HasStarted) return null;
                return ctx.Session;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SafeSession] Exception: {ex.Message}");
                return null;
            }
        }
    }

    public async Task SetAppStateToSession(AppState state)
    {
        try
        {
            var session = SafeSession;
            if (session != null)
            {
                var jsonState = JsonSerializer.Serialize(state);
                session.SetString("AppState", jsonState);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SetAppStateToSession] Exception: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    public void DeleteAppStateFromSession()
    {
        try
        {
            SafeSession?.Remove("AppState");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DeleteAppStateFromSession] Exception: {ex.Message}");
        }
    }

    public Task<AppState> GetAppStateFromSession()
    {
        try
        {
            var session = SafeSession;
            var jsonState = session?.GetString("AppState");

            if (!string.IsNullOrEmpty(jsonState))
            {
                var appState = JsonSerializer.Deserialize<AppState>(jsonState);
                if (appState != null)
                    return Task.FromResult(appState);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetAppStateFromSession] Exception: {ex.Message}");
        }

        return Task.FromResult(new AppState());
    }

    public async Task SetInitalAppStateToSession(AppState state)
    {
        try
        {
            var session = SafeSession;
            if (session != null)
            {
                var jsonState = JsonSerializer.Serialize(state);
                session.SetString("InitalAppState", jsonState);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SetInitalAppStateToSession] Exception: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    public async Task<AppState?> GetInitalAppStateFromSession()
    {
        await Task.Yield(); // satisfy async

        try
        {
            var session = SafeSession;
            var jsonState = session?.GetString("InitalAppState");

            if (!string.IsNullOrEmpty(jsonState))
            {
                return JsonSerializer.Deserialize<AppState>(jsonState);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetInitalAppStateFromSession] Exception: {ex.Message}");
        }

        return null;
    }
}
