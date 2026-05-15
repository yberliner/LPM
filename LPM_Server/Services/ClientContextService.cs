namespace LPM.Services;

/// <summary>
/// Per-circuit shared state. Scoped lifetime means one instance per Blazor circuit, so
/// any component in the circuit sees the same data.
///
/// LpmCircuitHandler populates ClientIp / UserAgent in its constructor — that's the only
/// point in a circuit's lifetime where IHttpContextAccessor.HttpContext is reliably
/// non-null (the SignalR negotiate request). After that, in-app navigation is pure
/// SignalR and HttpContext is null, so attempting to capture from inside a page's
/// OnInitializedAsync (e.g. Backup.razor) is fragile and breaks for in-app navigation.
///
/// ActiveBackupRunId is used to bridge two scenarios:
///   1) Component teardown via navigation: don't mark the run as interrupted just
///      because the user navigated away — the JS-side file backup continues, and on
///      return we want to keep heartbeating the same row. The new component reads
///      the runId from here.
///   2) True circuit close (browser closed, network gone past retention): the JS-side
///      is dead. LpmCircuitHandler.OnCircuitClosedAsync inspects this service and, if
///      a run is still un-finalized, marks it interrupted_disconnect.
/// </summary>
public sealed class ClientContextService
{
    public string? ClientIp        { get; set; }
    public string? UserAgent       { get; set; }
    public int?    ActiveBackupRunId { get; set; }
    public bool    BackupRunFinalized { get; set; }
}
