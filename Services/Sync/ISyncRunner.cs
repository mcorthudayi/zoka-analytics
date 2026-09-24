namespace ZokaAnalytics.Services.Sync;

public record SyncStepResult(string Step, bool Success, int ItemsProcessed, string? Message);

public record SyncSummary(
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    int ApiCallsUsed,
    IReadOnlyList<SyncStepResult> Steps);

public interface ISyncRunner
{
    Task<SyncSummary> RunFullSyncAsync(CancellationToken ct = default);
    Task<int> RefreshLiveMatchesAsync(CancellationToken ct = default);
}