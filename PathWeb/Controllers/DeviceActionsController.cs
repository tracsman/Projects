using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class DeviceActionsController : BaseController
{
    private readonly LabConfigContext _context;
    private readonly ILogger<DeviceActionsController> _logger;
    private readonly SshService _sshService;
    private readonly DeviceActionRunTracker _tracker;
    private readonly IServiceScopeFactory _scopeFactory;

    public DeviceActionsController(
        LabConfigContext context,
        ILogger<DeviceActionsController> logger,
        SshService sshService,
        DeviceActionRunTracker tracker,
        IServiceScopeFactory scopeFactory)
    {
        _context = context;
        _logger = logger;
        _sshService = sshService;
        _tracker = tracker;
        _scopeFactory = scopeFactory;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyOffDevice(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Name == configType);
        if (device == null || string.IsNullOrEmpty(device.MgmtIpv4))
        {
            var applyRun = await RecordDeviceActionRunAsync(tenantGuid, configType, "Apply", false, "Apply Failed", $"Device '{configType}' not found or has no management IP.");
            return Json(new { success = false, output = $"Device '{configType}' not found or has no management IP.", applyRun });
        }

        var searchPattern = $"Cust{tenant.TenantId}";
        var platform = PlatformDetector.DetectPlatform(configType);

        var command = PlatformDetector.GetShowCommand(platform, searchPattern);
        if (command == null)
            return Json(new { success = false, output = $"Unknown platform for device '{configType}'." });

        _logger.LogInformation("VerifyOffDevice: {Device} ({Host}) searching for '{Pattern}' by {User}",
            configType, device.MgmtIpv4, searchPattern, GetUserEmail());

        var (sshSuccess, output) = await _sshService.RunCommandAsync(device.MgmtIpv4, 22, command);
        if (!sshSuccess)
            return Json(new { success = false, output = $"SSH failed: {output}" });

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matchCount = lines.Length;

        _logger.LogInformation("VerifyOffDevice: {Device} returned {MatchCount} matches for '{Pattern}'",
            configType, matchCount, searchPattern);

        return Json(new
        {
            success = true,
            deviceName = configType,
            searchPattern,
            matchCount,
            output = matchCount == 0
                ? $"✔ Clean — no references to '{searchPattern}' found on {configType}."
                : output
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompareToDevice(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        var config = await _context.Configs.FirstOrDefaultAsync(
            c => c.TenantGuid == tenantGuid && c.ConfigVersion == tenant.ConfigVersion && c.ConfigType == configType);
        if (config == null || string.IsNullOrEmpty(config.Config1))
            return Json(new { success = false, output = $"No stored config found for '{configType}'." });

        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Name == configType);
        if (device == null || string.IsNullOrEmpty(device.MgmtIpv4))
        {
            var output = $"Device '{configType}' not found or has no management IP.";
            var applyRun = await RecordDeviceActionRunAsync(tenantGuid, configType, "Apply", false, "Apply Failed", output);
            return Json(new { success = false, output, applyRun });
        }

        var searchPattern = $"Cust{tenant.TenantId}";
        var platform = PlatformDetector.DetectPlatform(configType);

        var command = PlatformDetector.GetShowCommand(platform, searchPattern);
        if (command == null)
            return Json(new { success = false, output = $"Unknown platform for device '{configType}'." });

        _logger.LogInformation("CompareToDevice: {Device} ({Host}) comparing config by {User}",
            configType, device.MgmtIpv4, GetUserEmail());

        var (sshSuccess, sshOutput) = await _sshService.RunCommandAsync(device.MgmtIpv4, 22, command);
        if (!sshSuccess)
            return Json(new { success = false, output = $"SSH failed: {sshOutput}" });

        var storedSet = NormalizeConfigLines(config.Config1, platform);
        var deviceSet = NormalizeConfigLines(sshOutput, platform);
        var storedOrigs = OriginalCaseConfigLines(config.Config1, platform);
        var deviceOrigs = OriginalCaseConfigLines(sshOutput, platform);

        var missingFromDevice = storedSet.Except(deviceSet)
            .Select(k => storedOrigs.TryGetValue(k, out var o) ? o : k)
            .OrderBy(l => l).ToList();
        var extraOnDevice = deviceSet.Except(storedSet)
            .Select(k => deviceOrigs.TryGetValue(k, out var o) ? o : k)
            .OrderBy(l => l).ToList();
        var matchedCount = storedSet.Intersect(deviceSet).Count();
        var isMatch = missingFromDevice.Count == 0 && extraOnDevice.Count == 0;

        _logger.LogInformation("CompareToDevice: {Device} — stored:{StoredCount} device:{DeviceCount} matched:{Matched} missing:{Missing} extra:{Extra}",
            configType, storedSet.Count, deviceSet.Count, matchedCount, missingFromDevice.Count, extraOnDevice.Count);

        return Json(new
        {
            success = true,
            deviceName = configType,
            storedLineCount = storedSet.Count,
            deviceLineCount = deviceSet.Count,
            matchedCount,
            missingFromDevice,
            extraOnDevice,
            isMatch
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApplyToDevice(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        var existing = _tracker.GetActive(tenantGuid, configType);
        if (existing != null)
            return Json(new { success = false, output = $"An {existing.ActionType} run is already in progress on {configType}.", runId = existing.RunId, actionType = existing.ActionType });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        var config = await _context.Configs.FirstOrDefaultAsync(
            c => c.TenantGuid == tenantGuid && c.ConfigVersion == tenant.ConfigVersion && c.ConfigType == configType);
        if (config == null || string.IsNullOrEmpty(config.Config1))
            return Json(new { success = false, output = $"No stored config found for '{configType}'." });

        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Name == configType);
        if (device == null || string.IsNullOrEmpty(device.MgmtIpv4))
            return Json(new { success = false, output = $"Device '{configType}' not found or has no management IP." });

        var platform = PlatformDetector.DetectPlatform(configType);
        var searchPattern = $"Cust{tenant.TenantId}";
        var showCommand = PlatformDetector.GetShowCommand(platform, searchPattern);
        if (showCommand == null)
        {
            var applyRun = await RecordDeviceActionRunAsync(tenantGuid, configType, "Apply", false, "Apply Failed", $"Unknown platform for device '{configType}'.");
            return Json(new { success = false, output = $"Unknown platform for device '{configType}'.", applyRun });
        }

        var runId = $"apply-{configType}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var storedConfig = config.Config1;
        var host = device.MgmtIpv4!;
        var tenantId = tenant.TenantId;
        var submittedBy = GetUserEmail();

        var run = new DeviceActionRunInfo
        {
            RunId = runId,
            TenantGuid = tenantGuid,
            ConfigType = configType,
            ActionType = "Apply",
            SubmittedBy = submittedBy,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceName = configType
        };
        _tracker.Track(runId, run);

        _logger.LogInformation("ApplyToDevice.Submit: {Device} ({Host}) run {RunId} by {User}", configType, host, runId, submittedBy);

        _ = Task.Run(async () =>
        {
            try
            {
                var (sshSuccess, sshOutput) = await _sshService.RunCommandAsync(host, 22, showCommand);
                if (!sshSuccess)
                {
                    await CompleteRunAsync(run, false, "Apply Failed", $"SSH failed during comparison: {sshOutput}");
                    return;
                }

                var storedSet = NormalizeConfigLines(storedConfig, platform);
                var deviceSet = NormalizeConfigLines(sshOutput, platform);
                var missingKeys = storedSet.Except(deviceSet).ToHashSet();

                if (missingKeys.Count == 0)
                {
                    await CompleteRunAsync(run, true, "In Sync", $"Already in sync — nothing to apply on {configType}.");
                    return;
                }

                List<string> linesToApply;
                if (platform == "Juniper")
                {
                    var originalLines = OriginalCaseConfigLines(storedConfig, platform);
                    linesToApply = missingKeys
                        .Select(key => originalLines.TryGetValue(key, out var orig) ? orig : key)
                        .ToList();
                }
                else
                {
                    linesToApply = BuildCiscoAddLines(storedConfig, missingKeys, platform);
                }

                _logger.LogInformation("ApplyToDevice.Run: {Device} ({Host}) applying {Count} lines (run {RunId})",
                    configType, host, linesToApply.Count, runId);

                var (applySuccess, transcript, _) =
                    await _sshService.RunConfigSessionAsync(host, 22, linesToApply, platform);

                if (!applySuccess)
                {
                    _logger.LogWarning("ApplyToDevice.Run: {Device} config session failed (run {RunId})", configType, runId);
                    await CompleteRunAsync(run, false, "Apply Failed", transcript, transcript: transcript, appliedLines: linesToApply);
                    return;
                }

                _logger.LogInformation("ApplyToDevice.Run: {Device} successfully applied {Count} lines (run {RunId})",
                    configType, linesToApply.Count, runId);
                await CompleteRunAsync(run, true, "Applied", transcript, transcript: transcript, appliedLines: linesToApply, appliedCount: linesToApply.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ApplyToDevice.Run: {Device} background task failed (run {RunId})", configType, runId);
                await CompleteRunAsync(run, false, "Apply Failed", $"Background execution failed: {ex.Message}");
            }
        });

        return Json(new
        {
            success = true,
            runId,
            status = run.Status,
            actionType = run.ActionType,
            deviceName = configType
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PatchDevice(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        var existing = _tracker.GetActive(tenantGuid, configType);
        if (existing != null)
            return Json(new { success = false, output = $"An {existing.ActionType} run is already in progress on {configType}.", runId = existing.RunId, actionType = existing.ActionType });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        var config = await _context.Configs.FirstOrDefaultAsync(
            c => c.TenantGuid == tenantGuid && c.ConfigVersion == tenant.ConfigVersion && c.ConfigType == configType);
        if (config == null || string.IsNullOrEmpty(config.Config1))
            return Json(new { success = false, output = $"No stored config found for '{configType}'." });

        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Name == configType);
        if (device == null || string.IsNullOrEmpty(device.MgmtIpv4))
            return Json(new { success = false, output = $"Device '{configType}' not found or has no management IP." });

        var platform = PlatformDetector.DetectPlatform(configType);
        var searchPattern = $"Cust{tenant.TenantId}";
        var showCommand = PlatformDetector.GetShowCommand(platform, searchPattern);
        if (showCommand == null)
            return Json(new { success = false, output = $"Unknown platform for device '{configType}'." });

        var runId = $"patch-{configType}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var storedConfig = config.Config1;
        var host = device.MgmtIpv4!;
        var tenantId = tenant.TenantId;
        var submittedBy = GetUserEmail();

        var run = new DeviceActionRunInfo
        {
            RunId = runId,
            TenantGuid = tenantGuid,
            ConfigType = configType,
            ActionType = "Patch",
            SubmittedBy = submittedBy,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceName = configType
        };
        _tracker.Track(runId, run);

        _logger.LogInformation("PatchDevice.Submit: {Device} ({Host}) run {RunId} by {User}", configType, host, runId, submittedBy);

        _ = Task.Run(async () =>
        {
            try
            {
                var (sshSuccess, sshOutput) = await _sshService.RunCommandAsync(host, 22, showCommand);
                if (!sshSuccess)
                {
                    await CompleteRunAsync(run, false, "Patch Failed", $"SSH failed during comparison: {sshOutput}");
                    return;
                }

                var storedSet = NormalizeConfigLines(storedConfig, platform);
                var deviceSet = NormalizeConfigLines(sshOutput, platform);
                var missingKeys = storedSet.Except(deviceSet).ToHashSet();
                var extraKeys = deviceSet.Except(storedSet).ToHashSet();

                if (missingKeys.Count == 0 && extraKeys.Count == 0)
                {
                    await CompleteRunAsync(run, true, "In Sync", $"Already in sync — nothing to patch on {configType}.");
                    return;
                }

                var storedOriginals = OriginalCaseConfigLines(storedConfig, platform);
                var deviceOriginals = OriginalCaseConfigLines(sshOutput, platform);
                var configLines = new List<string>();
                var addedLines = new List<string>();
                var removedLines = new List<string>();

                if (platform == "Juniper")
                {
                    foreach (var key in missingKeys)
                    {
                        var line = storedOriginals.TryGetValue(key, out var orig) ? orig : key;
                        configLines.Add(line);
                        addedLines.Add(line);
                    }
                }
                else
                {
                    var ciscoAdds = BuildCiscoAddLines(storedConfig, missingKeys, platform);
                    foreach (var line in ciscoAdds)
                    {
                        configLines.Add(line);
                        addedLines.Add(line);
                    }
                }

                if (platform == "Juniper")
                {
                    AppendJuniperDeletes(extraKeys, deviceSet, deviceOriginals, tenantId,
                        configLines, removedLines);
                }
                else
                {
                    foreach (var key in extraKeys)
                    {
                        var line = deviceOriginals.TryGetValue(key, out var orig) ? orig : key;
                        var removeLine = "no " + line;
                        configLines.Add(removeLine);
                        removedLines.Add(removeLine);
                    }
                }

                _logger.LogInformation("PatchDevice.Run: {Device} ({Host}) patching +{AddCount}/-{RemoveCount} lines (run {RunId})",
                    configType, host, addedLines.Count, removedLines.Count, runId);

                var (patchSuccess, transcript, _) =
                    await _sshService.RunConfigSessionAsync(host, 22, configLines, platform);

                if (!patchSuccess)
                {
                    _logger.LogWarning("PatchDevice.Run: {Device} config session failed (run {RunId})", configType, runId);
                    await CompleteRunAsync(run, false, "Patch Failed", transcript, transcript: transcript, appliedLines: addedLines, removedLines: removedLines);
                    return;
                }

                _logger.LogInformation("PatchDevice.Run: {Device} successfully patched +{AddCount}/-{RemoveCount} lines (run {RunId})",
                    configType, addedLines.Count, removedLines.Count, runId);
                await CompleteRunAsync(run, true, "Patched", transcript, transcript: transcript,
                    appliedLines: addedLines, removedLines: removedLines,
                    appliedCount: addedLines.Count, removedCount: removedLines.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PatchDevice.Run: {Device} background task failed (run {RunId})", configType, runId);
                await CompleteRunAsync(run, false, "Patch Failed", $"Background execution failed: {ex.Message}");
            }
        });

        return Json(new
        {
            success = true,
            runId,
            status = run.Status,
            actionType = run.ActionType,
            deviceName = configType
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PreviewRemoveFromDevice(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        var backoutConfigType = $"{configType}-out";
        var backout = await _context.Configs.FirstOrDefaultAsync(
            c => c.TenantGuid == tenantGuid && c.ConfigVersion == tenant.ConfigVersion && c.ConfigType == backoutConfigType);
        if (backout == null || string.IsNullOrEmpty(backout.Config1))
            return Json(new { success = false, output = $"No backout config found for '{configType}'. Regenerate config to create it." });

        var platform = PlatformDetector.DetectPlatform(configType);
        var lines = backout.Config1
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("#"))
            .ToList();

        if (lines.Count == 0)
            return Json(new { success = false, output = $"Backout config for '{configType}' is empty — nothing to remove." });

        _logger.LogInformation("PreviewRemoveFromDevice: {Device} has {Count} backout lines, previewed by {User}",
            configType, lines.Count, GetUserEmail());

        return Json(new
        {
            success = true,
            deviceName = configType,
            platform,
            removeLines = lines,
            removeCount = lines.Count
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveFromDevice(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        var existing = _tracker.GetActive(tenantGuid, configType);
        if (existing != null)
            return Json(new { success = false, output = $"An {existing.ActionType} run is already in progress on {configType}.", runId = existing.RunId, actionType = existing.ActionType });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        var backoutConfigType = $"{configType}-out";
        var backout = await _context.Configs.FirstOrDefaultAsync(
            c => c.TenantGuid == tenantGuid && c.ConfigVersion == tenant.ConfigVersion && c.ConfigType == backoutConfigType);
        if (backout == null || string.IsNullOrEmpty(backout.Config1))
            return Json(new { success = false, output = $"No backout config found for '{configType}'." });

        var device = await _context.Devices.FirstOrDefaultAsync(d => d.Name == configType);
        if (device == null || string.IsNullOrEmpty(device.MgmtIpv4))
            return Json(new { success = false, output = $"Device '{configType}' not found or has no management IP." });

        var platform = PlatformDetector.DetectPlatform(configType);
        var lines = backout.Config1
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith("#"))
            .ToList();

        if (lines.Count == 0)
            return Json(new { success = true, removed = false, output = $"Backout config for '{configType}' is empty — nothing to remove." });

        var runId = $"remove-{configType}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        var host = device.MgmtIpv4!;
        var submittedBy = GetUserEmail();

        var run = new DeviceActionRunInfo
        {
            RunId = runId,
            TenantGuid = tenantGuid,
            ConfigType = configType,
            ActionType = "Remove",
            SubmittedBy = submittedBy,
            StartedAt = DateTimeOffset.UtcNow,
            DeviceName = configType
        };
        _tracker.Track(runId, run);

        _logger.LogInformation("RemoveFromDevice.Submit: {Device} ({Host}) removing {Count} lines run {RunId} by {User}",
            configType, host, lines.Count, runId, submittedBy);

        _ = Task.Run(async () =>
        {
            try
            {
                var (removeSuccess, transcript, _) =
                    await _sshService.RunConfigSessionAsync(host, 22, lines, platform);

                if (!removeSuccess)
                {
                    _logger.LogWarning("RemoveFromDevice.Run: {Device} config session failed (run {RunId})", configType, runId);
                    await CompleteRunAsync(run, false, "Remove Failed", transcript, transcript: transcript, removedLines: lines);
                    return;
                }

                _logger.LogInformation("RemoveFromDevice.Run: {Device} successfully removed {Count} lines (run {RunId})",
                    configType, lines.Count, runId);
                await CompleteRunAsync(run, true, "Removed", transcript, transcript: transcript, removedLines: lines, removedCount: lines.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RemoveFromDevice.Run: {Device} background task failed (run {RunId})", configType, runId);
                await CompleteRunAsync(run, false, "Remove Failed", $"Background execution failed: {ex.Message}");
            }
        });

        return Json(new
        {
            success = true,
            runId,
            status = run.Status,
            actionType = run.ActionType,
            deviceName = configType
        });
    }

    private static HashSet<string> NormalizeConfigLines(string text, string platform)
    {
        var commentPrefixes = platform == "Juniper"
            ? new[] { "#", "##" }
            : new[] { "!", "#" };

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !commentPrefixes.Any(p => line.StartsWith(p)))
            .Select(line => NormalizeLineForCompare(line, platform))
            .ToHashSet();
    }

    private static Dictionary<string, string> OriginalCaseConfigLines(string text, string platform)
    {
        var commentPrefixes = platform == "Juniper"
            ? new[] { "#", "##" }
            : new[] { "!", "#" };

        var result = new Dictionary<string, string>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (commentPrefixes.Any(p => line.StartsWith(p)))
                continue;
            var key = NormalizeLineForCompare(line, platform);
            result.TryAdd(key, line);
        }
        return result;
    }

    /// <summary>
    /// Normalizes a single config line for set-based comparison. Lowercases everything, and on
    /// Juniper strips trailing /32 (IPv4) and /128 (IPv6) host masks because Junos auto-appends
    /// those when echoing host addresses back via `display set`. Without this, a stored line
    /// `set ... destination-address 1.2.3.4` will not match the device's `... 1.2.3.4/32` even
    /// though they are semantically identical.
    /// </summary>
    private static string NormalizeLineForCompare(string line, string platform)
    {
        line = line.ToLowerInvariant();
        if (platform == "Juniper")
        {
            line = System.Text.RegularExpressions.Regex.Replace(line, @"(\d+\.\d+\.\d+\.\d+)/32\b", "$1");
            line = System.Text.RegularExpressions.Regex.Replace(line, @"([0-9a-f:]*:+[0-9a-f:]*)/128\b", "$1");
        }
        return line;
    }

    /// <summary>
    /// Walks a Cisco stored config in source order and emits the lines whose normalized form is in
    /// <paramref name="missingKeys"/>, prepending each indented child line's full ancestor stanza
    /// chain (in outer-to-inner order) when crossing into a new context. NX-OS / IOS-XE require
    /// commands like `name Cust50` to be issued inside their `vlan 50` parent context, not at the
    /// global `(config)#` prompt where they are rejected. Repeated emission of the same ancestor
    /// chain is suppressed so that consecutive children of the same stanza only re-issue the
    /// parent once. Top-level missing lines (e.g., a brand-new `vlan 50` stanza) are emitted
    /// directly with no parent prefix.
    /// </summary>
    private static List<string> BuildCiscoAddLines(string storedConfig, HashSet<string> missingKeys, string platform)
    {
        var result = new List<string>();
        var stack = new Stack<(int indent, string line)>();
        var lastChain = new List<string>();

        foreach (var rawLine in storedConfig.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                continue;

            var trimmed = rawLine.TrimStart();
            if (trimmed.StartsWith('#') || trimmed.StartsWith('!'))
                continue;

            var indent = rawLine.Length - trimmed.Length;
            var line = trimmed.TrimEnd();
            var key = NormalizeLineForCompare(line, platform);

            while (stack.Count > 0 && stack.Peek().indent >= indent)
                stack.Pop();

            if (missingKeys.Contains(key))
            {
                var newChain = stack.Reverse().Select(a => a.line).ToList();
                var common = 0;
                while (common < lastChain.Count && common < newChain.Count &&
                       string.Equals(lastChain[common], newChain[common], StringComparison.OrdinalIgnoreCase))
                {
                    common++;
                }
                for (var i = common; i < newChain.Count; i++)
                    result.Add(newChain[i]);
                result.Add(line);

                lastChain = newChain;
                lastChain.Add(line);
            }

            stack.Push((indent, line));
        }

        return result;
    }

    /// <summary>
    /// Builds Junos `delete` lines for extras. Where every device line we know about under a
    /// tenant-specific parent (`Cust{id}` variants or `unit {id}`) is being removed, emits a
    /// single `delete <parent>` rather than per-leaf deletes. This avoids commit-check failures
    /// like "Missing mandatory statement: 'match'" that occur when Junos sees a parent stanza
    /// (e.g., `rule Cust50_11`) with all its required children gone but the parent still present.
    /// Per-leaf deletes are kept whenever any leaf under the parent must remain.
    /// </summary>
    private static void AppendJuniperDeletes(
        HashSet<string> extraKeys,
        HashSet<string> deviceSet,
        Dictionary<string, string> deviceOriginals,
        int tenantId,
        List<string> configLines,
        List<string> removedLines)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var origByGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var extraKey in extraKeys)
        {
            var origLine = deviceOriginals.TryGetValue(extraKey, out var orig) ? orig : extraKey;
            var stanzaOrig = FindTenantStanzaPrefix(origLine, tenantId);
            var stanzaKey = NormalizeLineForCompare(stanzaOrig, "Juniper");

            if (!groups.TryGetValue(stanzaKey, out var list))
            {
                list = new List<string>();
                groups[stanzaKey] = list;
                origByGroup[stanzaKey] = stanzaOrig;
            }
            list.Add(extraKey);
        }

        foreach (var (stanzaKey, leafKeys) in groups)
        {
            var stanzaWithSpace = stanzaKey + " ";
            var hasKeepers = deviceSet.Any(d =>
                !extraKeys.Contains(d) &&
                (d.Equals(stanzaKey, StringComparison.OrdinalIgnoreCase) ||
                 d.StartsWith(stanzaWithSpace, StringComparison.OrdinalIgnoreCase)));

            var stanzaIsLeaf = leafKeys.Count == 1 &&
                               leafKeys[0].Equals(stanzaKey, StringComparison.OrdinalIgnoreCase);

            if (!hasKeepers && !stanzaIsLeaf)
            {
                var deletePath = origByGroup[stanzaKey];
                if (deletePath.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                    deletePath = deletePath[4..];
                var removeLine = "delete " + deletePath;
                configLines.Add(removeLine);
                removedLines.Add(removeLine);
            }
            else
            {
                foreach (var leafKey in leafKeys)
                {
                    var line = deviceOriginals.TryGetValue(leafKey, out var origLeaf) ? origLeaf : leafKey;
                    if (line.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                        line = line[4..];
                    var removeLine = "delete " + line;
                    configLines.Add(removeLine);
                    removedLines.Add(removeLine);
                }
            }
        }
    }

    /// <summary>
    /// Returns the longest token-aligned prefix of a Juniper `set ...` line that ends with a
    /// tenant-specific token (any token containing `Cust{id}` as a whole word, including variants
    /// like `Cust50_11`, `Cust50-onprem`, or `to-Cust50-instance`; or the `{id}` token immediately
    /// after `unit`). Returns the line unchanged when no marker is found, or when the marker is
    /// already the last token (no collapse possible).
    /// </summary>
    private static string FindTenantStanzaPrefix(string line, int tenantId)
    {
        if (!line.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
            return line;

        var tokens = line.Split(' ');
        var custPattern = new System.Text.RegularExpressions.Regex(
            $@"(^|[^a-z0-9])cust{tenantId}([^a-z0-9]|$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var tenantIdStr = tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var lastMarker = -1;
        for (var i = tokens.Length - 1; i >= 1; i--)
        {
            if (custPattern.IsMatch(tokens[i]))
            {
                lastMarker = i;
                break;
            }
            if (tokens[i] == tenantIdStr && tokens[i - 1].Equals("unit", StringComparison.OrdinalIgnoreCase))
            {
                lastMarker = i;
                break;
            }
        }

        if (lastMarker < 0 || lastMarker == tokens.Length - 1)
            return line;

        return string.Join(" ", tokens.Take(lastMarker + 1));
    }

    private async Task<object> RecordDeviceActionRunAsync(Guid tenantGuid, string configType, string actionType, bool success, string status, string output)
    {
        var run = new DeviceActionRun
        {
            DeviceActionRunId = Guid.NewGuid(),
            TenantGuid = tenantGuid,
            ConfigType = configType,
            ActionType = actionType,
            Success = success,
            Status = status,
            SubmittedBy = GetUserEmail(),
            SubmittedDate = DateTime.UtcNow,
            Output = output
        };

        _context.DeviceActionRuns.Add(run);
        await _context.SaveChangesAsync();
        return ToClientModel(run);
    }

    /// <summary>
    /// Called from background tasks to finalize a run in the tracker and persist a row to the
    /// DeviceActionRuns table so the card's badge/summary survives an app restart or page reload.
    /// Uses IServiceScopeFactory because the original request scope is gone by the time this runs.
    /// </summary>
    private async Task CompleteRunAsync(
        DeviceActionRunInfo run,
        bool success,
        string status,
        string output,
        string? transcript = null,
        List<string>? appliedLines = null,
        List<string>? removedLines = null,
        int? appliedCount = null,
        int? removedCount = null)
    {
        run.Success = success;
        run.Status = status;
        run.Output = output;
        run.Transcript = transcript;
        run.AppliedLines = appliedLines;
        run.RemovedLines = removedLines;
        run.AppliedCount = appliedCount;
        run.RemovedCount = removedCount;
        run.CompletedAt = DateTimeOffset.UtcNow;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabConfigContext>();
            db.DeviceActionRuns.Add(new DeviceActionRun
            {
                DeviceActionRunId = Guid.NewGuid(),
                TenantGuid = run.TenantGuid,
                ConfigType = run.ConfigType,
                ActionType = run.ActionType,
                Success = success,
                Status = status,
                SubmittedBy = run.SubmittedBy,
                SubmittedDate = run.StartedAt.UtcDateTime,
                Output = output
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist DeviceActionRun for {ConfigType} run {RunId}", run.ConfigType, run.RunId);
        }
    }

    [HttpGet]
    public IActionResult Status(string runId)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            return Json(new { success = false, output = "Permission denied." });

        if (string.IsNullOrWhiteSpace(runId))
            return Json(new { success = false, output = "Missing run ID." });

        var run = _tracker.Get(runId);
        if (run == null)
            return Json(new { success = false, output = "Run not found. It may have expired after an app restart." });

        return Json(new
        {
            success = true,
            runId = run.RunId,
            configType = run.ConfigType,
            actionType = run.ActionType,
            deviceName = run.DeviceName,
            status = run.Status,
            success_ = run.Success,
            isTerminal = run.CompletedAt != null,
            startedAt = run.StartedAt,
            completedAt = run.CompletedAt,
            submittedBy = run.SubmittedBy,
            output = run.Output ?? string.Empty,
            transcript = run.Transcript ?? string.Empty,
            appliedLines = run.AppliedLines,
            removedLines = run.RemovedLines,
            appliedCount = run.AppliedCount,
            removedCount = run.RemovedCount
        });
    }

    private static object ToClientModel(DeviceActionRun run) => new
    {
        configType = run.ConfigType,
        actionType = run.ActionType,
        success = run.Success,
        status = run.Status,
        submittedBy = run.SubmittedBy,
        submittedDate = run.SubmittedDate,
        output = run.Output ?? string.Empty
    };
}
