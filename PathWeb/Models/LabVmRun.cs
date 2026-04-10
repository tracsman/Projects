namespace PathWeb.Models;

public class LabVmRun
{
    public Guid LabVmRunId { get; set; }

    public Guid TenantGuid { get; set; }

    public string RunId { get; set; } = null!;

    public bool Success { get; set; }

    public string Status { get; set; } = null!;

    public int RequestCount { get; set; }

    public string? CreatedVmNames { get; set; }

    public string SubmittedBy { get; set; } = null!;

    public DateTime SubmittedDate { get; set; }

    public DateTime? CompletedDate { get; set; }

    public string? Output { get; set; }
}
