using System.Collections.Concurrent;
using System.Text.Json;

namespace PathWeb.Services;

/// <summary>
/// In-memory tracker for Lab VM SSH spike runs. Stores run state so the launch
/// endpoint can return immediately and a lightweight status endpoint can report
/// progress without SSH round-trips or remote status files.
/// </summary>
public class LabVmRunTracker
{
    private readonly ConcurrentDictionary<string, LabVmRunInfo> _runs = new(StringComparer.OrdinalIgnoreCase);

    public void Track(string runId, LabVmRunInfo run) => _runs[runId] = run;

    public LabVmRunInfo? Get(string runId) => _runs.GetValueOrDefault(runId);
}

public class LabVmRunInfo
{
    public required string RunId { get; init; }
    public required string TenantName { get; init; }
    public required int TenantId { get; init; }
    public required string TargetServer { get; init; }
    public required string TargetHost { get; init; }
    public required int RequestCount { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    // Mutable state — updated by the background task
    public string Status { get; set; } = "Running";
    public DateTimeOffset? CompletedAt { get; set; }
    public JsonElement? Results { get; set; }
    public string? Error { get; set; }
    public string? RawOutput { get; set; }
}
