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
    /// Connects to a device, detects the platform from the SSH banner, runs the appropriate
    /// version command, and returns a short OS string like "Junos 21.1R1.11" or "NX-OS 9.3(8)".
    /// </summary>
    public async Task<(bool Success, string OsVersion)> DetectOsVersionAsync(string host)
    {
        try
        {
            var (username, password) = await GetCredentialsAsync();

            using var client = new SshClient(host, 22, username, password);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);

            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
                return (false, "Connection failed");

            var banner = client.ConnectionInfo.ServerVersion ?? "";
            _logger.LogInformation("SSH banner for {Host}: {Banner}", host, banner);

            string osVersion;

            if (banner.Contains("OpenSSH", StringComparison.OrdinalIgnoreCase))
            {
                // Juniper devices use OpenSSH
                var result = client.RunCommand("show version | match \"Junos:\"");
                var match = Regex.Match(result.Result, @"Junos:\s*(.+)");
                osVersion = match.Success ? $"Junos {match.Groups[1].Value.Trim()}" : "Junos (unknown)";
            }
            else if (banner.Contains("Cisco", StringComparison.OrdinalIgnoreCase))
            {
                // Try NX-OS first (Nexus switches)
                var result = client.RunCommand("show version | include \"NXOS\\|system:\"");
                var nxMatch = Regex.Match(result.Result, @"(?:NXOS|system).*?version\s+(\S+)", RegexOptions.IgnoreCase);
                if (nxMatch.Success)
                {
                    osVersion = $"NX-OS {nxMatch.Groups[1].Value}";
                }
                else
                {
                    // IOS-XE (ASR routers)
                    result = client.RunCommand("show version | include Version");
                    var iosMatch = Regex.Match(result.Result, @"Version\s+(\S+?)(?:,|\s|$)");
                    osVersion = iosMatch.Success ? $"IOS-XE {iosMatch.Groups[1].Value}" : "Cisco (unknown)";
                }
            }
            else
            {
                osVersion = $"Unknown ({banner})";
            }

            client.Disconnect();

            _logger.LogInformation("Detected OS for {Host}: {OsVersion}", host, osVersion);
            return (true, osVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OS detection failed for {Host}", host);
            return (false, $"Error: {ex.Message}");
        }
    }
}
