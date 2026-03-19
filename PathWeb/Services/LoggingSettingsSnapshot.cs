namespace PathWeb.Services;

public sealed class LoggingSettingsSnapshot
{
    public string DefaultLevel { get; set; } = "Information";

    public Dictionary<string, string> CategoryLevels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
