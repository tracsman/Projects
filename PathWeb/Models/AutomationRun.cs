namespace PathWeb.Models;

public class AutomationRun
{
    public Guid AutomationRunId { get; set; }

    public Guid TenantGuid { get; set; }

    public string ConfigType { get; set; } = null!;

    public string JobId { get; set; } = null!;

    public string RunbookName { get; set; } = null!;

    public string SubmittedBy { get; set; } = null!;

    public DateTime SubmittedDate { get; set; }

    public string Status { get; set; } = null!;

    public DateTime? LastStatusDate { get; set; }

    public string? LastOutput { get; set; }

    public string? LastException { get; set; }

    public string? PreparedScript { get; set; }

    public DateTime? CompletedDate { get; set; }
}
