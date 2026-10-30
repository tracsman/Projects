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
    /// <remarks>
    /// For Juniper, the regex also matches `unit {id} ` so that interface attribute lines
    /// (e.g., `set interfaces reth1 unit 50 vlan-id 50`) are returned alongside lines that
    /// literally contain `Cust{id}`. Without this, attribute lines under an interface unit
    /// are missed by `match Cust{id}` because only the description line carries the tenant name.
    /// The trailing space in `unit {id} ` prevents false matches on `unit {id}0` etc.
    ///
    /// For Cisco (NX-OS/IOS-XE), the regex also matches `vlan {id}` followed by a space or end
    /// of line, so VLAN stanza headers (e.g., `vlan 50`) are returned alongside lines that
    /// literally contain `Cust{id}`. The trailing `( |$)` guard prevents false matches like
    /// `vlan 500` when comparing tenant 50.
    ///
    /// IOS-XE treats quotes in pipe filters as literal characters, and tenant router configs are
    /// VRF-scoped (`vrf definition {id}`, sub-interfaces, BGP address-families) with most lines
    /// lacking the `Cust{id}` marker. `show running-config vrf {id}` returns all of those stanzas
    /// with their parent context; the tenant route-map is fetched separately.
    /// </remarks>
    public static string? GetShowCommand(string platform, string searchPattern) => platform switch
    {
        "Juniper" => $"show configuration | display set | match \"({searchPattern}|unit {ExtractTenantId(searchPattern)} )\"",
        "NX-OS" => $"show running-config | include \"({searchPattern}|vlan {ExtractTenantId(searchPattern)}( |$))\"",
        "IOS-XE" => $"show running-config vrf {ExtractTenantId(searchPattern)}",
        _ => null
    };

    /// <summary>
    /// IOS-XE commands that supplement <see cref="GetShowCommand"/> for tenant config outside the VRF.
    /// IOS regex `_` matches a delimiter or end of line, so `Cust10_` will not match `Cust100`.
    /// </summary>
    public static IReadOnlyList<string> GetIosXeSupplementalCommands(string searchPattern) =>
    [
        $"show running-config | section ^route-map {searchPattern}_"
    ];

    /// <summary>
    /// IOS-XE command for Verify Off Device that catches stray `Cust{id}` references outside the VRF.
    /// </summary>
    public static string GetIosXeReferenceCommand(string searchPattern) =>
        $"show running-config | include {searchPattern}_";

    /// <summary>
    /// Extracts the numeric tenant id from a `Cust{id}` search pattern (case-insensitive).
    /// Falls back to the original pattern if it does not match the expected shape.
    /// </summary>
    private static string ExtractTenantId(string searchPattern)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            searchPattern, @"^Cust(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : searchPattern;
    }
}
