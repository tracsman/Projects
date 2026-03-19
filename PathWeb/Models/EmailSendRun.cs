namespace PathWeb.Models;

public class EmailSendRun
{
    public Guid EmailSendRunId { get; set; }

    public Guid TenantGuid { get; set; }

    public string ConfigType { get; set; } = null!;

    public bool Success { get; set; }

    public string Status { get; set; } = null!;

    public string SubmittedBy { get; set; } = null!;

    public DateTime SubmittedDate { get; set; }

    public string? Recipient { get; set; }

    public string? Subject { get; set; }

    public string? Output { get; set; }
}
