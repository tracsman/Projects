namespace PathWeb.Services;

public static class AutomationRunAction
{
    private const string Marker = "# PathWeb selected action: ";

    public static bool IsValid(string configType, string? action) =>
        string.IsNullOrEmpty(action) || (configType == "ServiceProviderInstructions"
            ? action is "provision" or "deprovision"
            : (configType is "CreateERPowerShell" or "CreateAzurePowerShell") && action is "deploy" or "remove");

    public static string AddMarker(string script, string? action) =>
        script.TrimEnd() + "\r\n" + Marker + (string.IsNullOrEmpty(action) ? "unknown" : action);

    public static string GetAction(string? script)
    {
        if (string.IsNullOrWhiteSpace(script)) return "unknown";
        var lastLine = script.TrimEnd().Split('\n')[^1].TrimEnd('\r');
        if (!lastLine.StartsWith(Marker, StringComparison.Ordinal)) return "unknown";
        var action = lastLine[Marker.Length..];
        return action is "deploy" or "remove" or "provision" or "deprovision" ? action : "unknown";
    }
}
