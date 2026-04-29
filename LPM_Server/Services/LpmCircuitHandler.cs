using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LPM.Services;

public class LpmCircuitHandler : CircuitHandler
{
    private readonly UserActivityService _activitySvc;
    private readonly CircuitTrackingService _tracker;
    private readonly string _username;

    public LpmCircuitHandler(UserActivityService activitySvc, CircuitTrackingService tracker, IHttpContextAccessor httpContextAccessor)
    {
        _activitySvc = activitySvc;
        _tracker     = tracker;
        // Capture username now — HttpContext is only available during the initial HTTP connection
        _username = httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "";
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _tracker.Open(circuit.Id, _username);
        var isFirst = _activitySvc.TrackCircuitOpen(_username);
        if (isFirst && !string.IsNullOrEmpty(_username))
            _activitySvc.RecordActivity(_username, "Returned", "login");
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _tracker.ConnectionDown(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _tracker.ConnectionUp(circuit.Id);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _tracker.Close(circuit.Id);
        _activitySvc.RecordLogout(_username);
        return Task.CompletedTask;
    }
}
