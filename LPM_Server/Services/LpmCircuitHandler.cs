using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LPM.Services;

public class LpmCircuitHandler : CircuitHandler
{
    private readonly UserActivityService _activitySvc;
    private readonly CircuitTrackingService _tracker;
    private readonly ClientContextService _clientCtx;
    private readonly BackupHistoryService _backupHistory;
    private readonly string _username;

    public LpmCircuitHandler(
        UserActivityService activitySvc,
        CircuitTrackingService tracker,
        ClientContextService clientCtx,
        BackupHistoryService backupHistory,
        IHttpContextAccessor httpContextAccessor)
    {
        _activitySvc   = activitySvc;
        _tracker       = tracker;
        _clientCtx     = clientCtx;
        _backupHistory = backupHistory;
        // Capture username + client metadata now — HttpContext is only available during
        // the initial HTTP connection (the SignalR negotiate request). The scoped
        // ClientContextService keeps these alive for the rest of the circuit.
        var hctx = httpContextAccessor.HttpContext;
        _username = hctx?.User?.Identity?.Name ?? "";

        if (hctx != null)
        {
            try
            {
                var addr = hctx.Connection.RemoteIpAddress;
                if (addr != null)
                {
                    if (addr.IsIPv4MappedToIPv6) addr = addr.MapToIPv4();
                    _clientCtx.ClientIp = addr.ToString();
                }
                var ua = hctx.Request.Headers["User-Agent"].ToString();
                _clientCtx.UserAgent = string.IsNullOrWhiteSpace(ua) ? null : ua;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Circuits] Failed to capture client metadata: {ex.Message}");
            }
        }
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

        // If the user closed the browser (or otherwise dropped past SignalR retention)
        // while a backup was running and the JS side never reported completion, mark
        // the row as interrupted_disconnect now. Navigation-away inside the app does
        // NOT trigger this — only true circuit close does.
        if (_clientCtx.ActiveBackupRunId is int runId && runId > 0 && !_clientCtx.BackupRunFinalized)
        {
            try { _backupHistory.Finish(runId, BackupHistoryService.StatusInterruptedDisconnect, null); }
            catch (Exception ex) { Console.WriteLine($"[Backup] Circuit-close interrupted-disconnect finalize failed: {ex.Message}"); }
        }
        return Task.CompletedTask;
    }
}
