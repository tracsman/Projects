using System.Collections.Concurrent;
using System.Text.Json;

namespace PathWeb.Services;

/// <summary>
/// In-memory tracker for Lab VM runs. Stores run state so the launch
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
    public required string Lab { get; init; }
    public required string SubmittedBy { get; init; }
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Per-VM request tracking with individual server targets.</summary>
    public required List<LabVmRequestInfo> Requests { get; init; }

    // Mutable aggregate state — updated by the background task
    public string Status { get; set; } = "Running";
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}

public class LabVmRequestInfo
{
    public required string RequestId { get; init; }
    public required int Index { get; init; }
    public required string Os { get; init; }
    public required string TargetServer { get; init; }
    public required string TargetHost { get; init; }

    // Mutable state — updated per-request by the background task
    public string Status { get; set; } = "Pending";
    public string? Message { get; set; }
    public string? CreatedVmName { get; set; }
    public JsonElement? RawResult { get; set; }
}
