using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LPM.Services;

public class LpmCircuitHandler : CircuitHandler
{
    private readonly UserActivityService _activitySvc;
    private readonly string _username;

    public LpmCircuitHandler(UserActivityService activitySvc, IHttpContextAccessor httpContextAccessor)
    {
        _activitySvc = activitySvc;
        // Capture username now — HttpContext is only available during the initial HTTP connection
        _username = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "";
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _activitySvc.RecordLogin(_username);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _activitySvc.RecordLogout(_username);
        return Task.CompletedTask;
    }
}
