namespace PathWeb.Models;

public sealed class TenantConfigViewModel
{
    public Guid TenantGuid { get; set; }

    public string TenantName { get; set; } = string.Empty;

    public string Contacts { get; set; } = string.Empty;

    public bool IsInactive { get; set; }

    public IReadOnlyList<Config> Configs { get; set; } = Array.Empty<Config>();

    public IReadOnlyDictionary<string, AutomationRun> AutomationRuns { get; set; } = new Dictionary<string, AutomationRun>();

    public IReadOnlyDictionary<string, IReadOnlyList<AutomationRun>> AutomationRunHistory { get; set; } = new Dictionary<string, IReadOnlyList<AutomationRun>>();

    public IReadOnlyDictionary<string, DeviceActionRun> DeviceApplyRuns { get; set; } = new Dictionary<string, DeviceActionRun>();

    public IReadOnlyDictionary<string, EmailSendRun> EmailSendRuns { get; set; } = new Dictionary<string, EmailSendRun>();

    public LabVmRun? LatestLabVmRun { get; set; }
}
