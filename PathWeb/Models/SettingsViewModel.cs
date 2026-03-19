namespace PathWeb.Models;

public class SettingsViewModel
{
    public bool AutoDeleteRunbooks { get; set; }

    public string AutomationRunbookType { get; set; } = string.Empty;

    public string LoggingDefaultLevel { get; set; } = "Information";

    public List<LoggingOverrideViewModel> LoggingOverrides { get; set; } = [];

    public bool CanEdit { get; set; }
}
