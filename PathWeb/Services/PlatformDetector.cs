namespace PathWeb.Services;

/// <summary>
/// Determines the device platform from the device name using naming conventions.
/// MX/SRX = Juniper, NX = Cisco NX-OS (Nexus), ASR/ISR/CSR/CAT = Cisco IOS-XE.
/// </summary>
public static class PlatformDetector
{
    public static string DetectPlatform(string deviceName)
    {
        var upper = deviceName.ToUpperInvariant();

        // Juniper: MX series routers, SRX series firewalls
        if (upper.Contains("-MX") || upper.Contains("-SRX"))
            return "Juniper";

        // Cisco Nexus: NX9K, NX3K, NX5K, etc.
        if (upper.Contains("-NX"))
            return "NX-OS";

        // Cisco IOS-XE: ASR, ISR, CSR, Cat series, Access-In (ISR routers)
        if (upper.Contains("-ASR") || upper.Contains("-ISR") || upper.Contains("-CSR") || upper.Contains("-CAT") || upper.Contains("-ACCESS-IN"))
            return "IOS-XE";

        return "Unknown";
    }

    /// <summary>
    /// Returns the platform-appropriate show command to grep for a search pattern in the running config.
    /// Returns null for unknown platforms.
    /// </summary>
    public static string? GetShowCommand(string platform, string searchPattern) => platform switch
    {
        "Juniper" => $"show configuration | display set | match {searchPattern}",
        "NX-OS" => $"show running-config | include {searchPattern}",
        "IOS-XE" => $"show running-config | include {searchPattern}",
        _ => null
    };
}
