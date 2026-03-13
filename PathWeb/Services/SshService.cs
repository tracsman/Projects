using System.Text;
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
    /// Opens an interactive shell session to apply configuration lines to a network device.
    /// Juniper: configure → load set terminal → commit check → show | compare → commit and-quit
    /// Cisco: configure terminal → apply lines → end → wr
    /// Returns success, the session transcript, and (for Juniper) the show|compare output.
    /// </summary>
    public async Task<(bool Success, string Output, string CompareOutput)> RunConfigSessionAsync(
        string host, int port, IReadOnlyList<string> configLines, string platform)
    {
        if (configLines.Count == 0)
            return (true, "No lines to apply.", "");

        try
        {
            var (username, password) = await GetCredentialsAsync();

            _logger.LogInformation("SSH config session to {Host}:{Port} as {User}, platform: {Platform}, lines: {Count}",
                host, port, username, platform, configLines.Count);

            using var client = new SshClient(host, port, username, password);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(10);

            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
                return (false, "Connection failed — client did not connect.", "");

            using var shell = client.CreateShellStream("config", 200, 50, 800, 600, 4096);
            var transcript = new StringBuilder();
            var compareOutput = "";

            // Wait for initial prompt
            await WaitForPromptAsync(shell, transcript, TimeSpan.FromSeconds(5));

            if (platform == "Juniper")
            {
                // Enter configuration mode
                await SendLineAsync(shell, "configure", transcript, TimeSpan.FromSeconds(5));

                // Load config lines via 'load set terminal' (terminated by Ctrl-D)
                await SendLineAsync(shell, "load set terminal", transcript, TimeSpan.FromSeconds(3));
                foreach (var line in configLines)
                {
                    await SendLineAsync(shell, line, transcript, TimeSpan.FromSeconds(2));
                }
                // Ctrl-D to end load set terminal input
                shell.Write("\x04");
                await WaitForPromptAsync(shell, transcript, TimeSpan.FromSeconds(5));

                // Commit check — validates without applying
                await SendLineAsync(shell, "commit check", transcript, TimeSpan.FromSeconds(30));
                var checkOutput = transcript.ToString();
                if (checkOutput.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                    !checkOutput.Contains("commit check succeeds", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("SSH config session commit check failed on {Host}", host);
                    await SendLineAsync(shell, "rollback 0", transcript, TimeSpan.FromSeconds(5));
                    await SendLineAsync(shell, "exit", transcript, TimeSpan.FromSeconds(3));
                    client.Disconnect();
                    return (false, $"Commit check failed:\n{transcript}", "");
                }

                // Show | compare — capture Junos's own diff
                var preCompareLength = transcript.Length;
                await SendLineAsync(shell, "show | compare", transcript, TimeSpan.FromSeconds(10));
                compareOutput = transcript.ToString()[preCompareLength..].Trim();

                // Commit and quit
                await SendLineAsync(shell, "commit and-quit", transcript, TimeSpan.FromSeconds(30));

                _logger.LogInformation("SSH config session committed on {Host}", host);
            }
            else
            {
                // Cisco NX-OS / IOS-XE
                await SendLineAsync(shell, "configure terminal", transcript, TimeSpan.FromSeconds(5));

                foreach (var line in configLines)
                {
                    await SendLineAsync(shell, line, transcript, TimeSpan.FromSeconds(3));
                }

                await SendLineAsync(shell, "end", transcript, TimeSpan.FromSeconds(3));

                // Save config using the 'wr' alias
                await SendLineAsync(shell, "wr", transcript, TimeSpan.FromSeconds(10));

                _logger.LogInformation("SSH config session applied and saved on {Host}", host);
            }

            client.Disconnect();

            var fullTranscript = transcript.ToString();

            // Check for Cisco error patterns in the transcript
            if (platform != "Juniper" &&
                Regex.IsMatch(fullTranscript, @"% Invalid|% Ambiguous|% Incomplete", RegexOptions.IgnoreCase))
            {
                return (false, $"Config errors detected:\n{fullTranscript}", "");
            }

            return (true, fullTranscript, compareOutput);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SSH config session failed to {Host}:{Port}", host, port);
            return (false, $"Error: {ex.Message}", "");
        }
    }

    private static async Task SendLineAsync(ShellStream shell, string command, StringBuilder transcript, TimeSpan timeout)
    {
        shell.WriteLine(command);
        await WaitForPromptAsync(shell, transcript, timeout);
    }

    private static async Task WaitForPromptAsync(ShellStream shell, StringBuilder transcript, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var buffer = new StringBuilder();

        while (DateTime.UtcNow < deadline)
        {
            var chunk = shell.Read();
            if (!string.IsNullOrEmpty(chunk))
            {
                buffer.Append(chunk);
                transcript.Append(chunk);

                // Detect common prompts: user@host>, user@host#, hostname#, hostname(config)#
                var text = buffer.ToString();
                if (Regex.IsMatch(text, @"[\$#>%]\s*$"))
                    return;
            }
            else
            {
                await Task.Delay(100);
            }
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
            var platform = PlatformDetector.DetectPlatform(deviceName);
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

    }
