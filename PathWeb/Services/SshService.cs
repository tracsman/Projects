using System.Text.RegularExpressions;
using Azure.Security.KeyVault.Secrets;
using Renci.SshNet;

namespace PathWeb.Services;

public class SshService
{
    private readonly IConfiguration _config;
    private readonly SecretClient _secretClient;
    private readonly ILogger<SshService> _logger;

    public SshService(IConfiguration config, SecretClient secretClient, ILogger<SshService> logger)
    {
        _config = config;
        _secretClient = secretClient;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves device credentials from Key Vault and app settings.
    /// Username comes from config (DeviceCredentials__Username), password from Key Vault secret with the same name.
    /// </summary>
    private async Task<(string Username, string Password)> GetCredentialsAsync()
    {
        var username = _config["DeviceCredentials:Username"]
            ?? throw new InvalidOperationException("DeviceCredentials:Username not configured.");

        var secret = await _secretClient.GetSecretAsync(username);
        return (username, secret.Value.Value);
    }

    /// <summary>
    /// Connects to a device via SSH using Key Vault credentials, runs a single command, and returns the output.
    /// </summary>
    public async Task<(bool Success, string Output)> RunCommandAsync(string host, int port, string command = "show version")
    {
        try
        {
            var (username, password) = await GetCredentialsAsync();

            _logger.LogInformation("SSH connecting to {Host}:{Port} as {User}", host, port, username);

            using var client = new SshClient(host, port, username, password);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);

            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
                return (false, "Connection failed — client did not connect.");

            _logger.LogInformation("SSH connected to {Host}, running command: {Command}", host, command);

            var result = client.RunCommand(command);

            client.Disconnect();

            if (result.ExitStatus != 0 && !string.IsNullOrEmpty(result.Error))
            {
                _logger.LogWarning("SSH command returned exit code {ExitCode}: {Error}", result.ExitStatus, result.Error);
                return (false, $"Exit code {result.ExitStatus}: {result.Error}");
            }

            _logger.LogInformation("SSH command succeeded on {Host}", host);
            return (true, result.Result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH connection failed to {Host}:{Port}", host, port);
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Connects to a device, runs the appropriate version command based on the device name,
    /// and returns a short OS string like "Junos 21.1R1.11" or "NX-OS 9.3(8)".
    /// </summary>
    public async Task<(bool Success, string OsVersion)> DetectOsVersionAsync(string host, string deviceName)
    {
        try
        {
            var (username, password) = await GetCredentialsAsync();

            using var client = new SshClient(host, 22, username, password);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);

            // Cisco IOS-XE (ASR) may need older key exchange algorithms
            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
                return (false, "Connection failed");

            _logger.LogInformation("Connected to {Host} ({Device}), detecting OS", host, deviceName);

            // Determine platform from device name
            var platform = DetectPlatform(deviceName);
            string osVersion;

            switch (platform)
            {
                case "Juniper":
                    var junosResult = client.RunCommand("show version | match \"Junos:\"");
                    var junosMatch = Regex.Match(junosResult.Result, @"Junos:\s*(.+)");
                    osVersion = junosMatch.Success ? $"Junos {junosMatch.Groups[1].Value.Trim()}" : "Junos (unknown)";
                    break;

                case "NX-OS":
                    var nxResult = client.RunCommand("show version");
                    // NX-9K format: "NXOS: version 7.0(3)I7(6)"
                    // NX-3K format: "system:    version 6.0(2)U6(9)"
                    var nxMatch = Regex.Match(nxResult.Result, @"(?:NXOS|system):\s*version\s+(\S+)", RegexOptions.IgnoreCase);
                    osVersion = nxMatch.Success ? $"NX-OS {nxMatch.Groups[1].Value}" : "NX-OS (unknown)";
                    break;

                case "IOS-XE":
                    var iosResult = client.RunCommand("show version");
                    var iosMatch = Regex.Match(iosResult.Result, @"Cisco IOS XE Software, Version\s+(\S+)");
                    osVersion = iosMatch.Success ? $"IOS-XE {iosMatch.Groups[1].Value}" : "IOS-XE (unknown)";
                    break;

                default:
                    osVersion = $"Unknown platform";
                    break;
            }

            client.Disconnect();

            _logger.LogInformation("Detected OS for {Device} ({Host}): {OsVersion}", deviceName, host, osVersion);
            return (true, osVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OS detection failed for {Device} ({Host})", deviceName, host);
            return (false, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Determines the device platform from the device name using naming conventions.
    /// MX/SRX = Juniper, NX = Cisco NX-OS (Nexus), ASR/ISR = Cisco IOS-XE.
    /// </summary>
    private static string DetectPlatform(string deviceName)
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
}
