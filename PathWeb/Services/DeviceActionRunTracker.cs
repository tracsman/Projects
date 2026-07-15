using System.Collections.Concurrent;

namespace PathWeb.Services;

/// <summary>
/// In-memory tracker for asynchronous device Apply/Patch/Remove runs. Enables the
/// submit endpoints to return immediately while background tasks perform the SSH
/// work, and lets the client poll a lightweight status endpoint for per-run progress
/// without blocking or bouncing through SSH again.
/// </summary>
public class DeviceActionRunTracker
{
    private readonly ConcurrentDictionary<string, DeviceActionRunInfo> _runs = new(StringComparer.OrdinalIgnoreCase);

    public void Track(string runId, DeviceActionRunInfo run) => _runs[runId] = run;

    public DeviceActionRunInfo? Get(string runId) => _runs.GetValueOrDefault(runId);

    /// <summary>
    /// Returns the most recent non-terminal run for the given tenant/configType, or null
    /// if there is no active run. Used to prevent overlapping runs on the same device.
    /// </summary>
    public DeviceActionRunInfo? GetActive(Guid tenantGuid, string configType)
    {
        foreach (var run in _runs.Values)
        {
            if (run.TenantGuid == tenantGuid &&
                string.Equals(run.ConfigType, configType, StringComparison.OrdinalIgnoreCase) &&
                run.CompletedAt == null)
            {
                return run;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the most recent run for the given tenant/configType regardless of state.
    /// Used by the client to reconnect an in-flight run on modal reopen.
    /// </summary>
    public DeviceActionRunInfo? GetLatest(Guid tenantGuid, string configType)
    {
        DeviceActionRunInfo? latest = null;
        foreach (var run in _runs.Values)
        {
            if (run.TenantGuid == tenantGuid &&
                string.Equals(run.ConfigType, configType, StringComparison.OrdinalIgnoreCase) &&
                (latest == null || run.StartedAt > latest.StartedAt))
            {
                latest = run;
            }
        }
        return latest;
    }
}

public class DeviceActionRunInfo
{
    public required string RunId { get; init; }
    public required Guid TenantGuid { get; init; }
    public required string ConfigType { get; init; }
    public required string ActionType { get; init; }
    public required string SubmittedBy { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    // Mutable state — updated by the background task
    public string Status { get; set; } = "Running";
    public bool? Success { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Output { get; set; }
    public string? Transcript { get; set; }
    public List<string>? AppliedLines { get; set; }
    public List<string>? RemovedLines { get; set; }
    public int? AppliedCount { get; set; }
    public int? RemovedCount { get; set; }
    public string DeviceName { get; init; } = string.Empty;
}
