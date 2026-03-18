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

    public DeviceActionsController(LabConfigContext context, ILogger<DeviceActionsController> logger, SshService sshService)
    {
        _context = context;
        _logger = logger;
        _sshService = sshService;
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
            var applyRun = await RecordDeviceApplyRunAsync(tenantGuid, configType, false, "Apply Failed", $"Device '{configType}' not found or has no management IP.");
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
            var applyRun = await RecordDeviceApplyRunAsync(tenantGuid, configType, false, "Apply Failed", output);
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

        var missingFromDevice = storedSet.Except(deviceSet).OrderBy(l => l).ToList();
        var extraOnDevice = deviceSet.Except(storedSet).OrderBy(l => l).ToList();
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

        var searchPattern = $"Cust{tenant.TenantId}";
        var platform = PlatformDetector.DetectPlatform(configType);

        var showCommand = PlatformDetector.GetShowCommand(platform, searchPattern);
        if (showCommand == null)
        {
            var applyRun = await RecordDeviceApplyRunAsync(tenantGuid, configType, false, "Apply Failed", $"Unknown platform for device '{configType}'.");
            return Json(new { success = false, output = $"Unknown platform for device '{configType}'.", applyRun });
        }

        var (sshSuccess, sshOutput) = await _sshService.RunCommandAsync(device.MgmtIpv4, 22, showCommand);
        if (!sshSuccess)
        {
            var output = $"SSH failed during comparison: {sshOutput}";
            var applyRun = await RecordDeviceApplyRunAsync(tenantGuid, configType, false, "Apply Failed", output);
            return Json(new { success = false, output, applyRun });
        }

        var storedSet = NormalizeConfigLines(config.Config1, platform);
        var deviceSet = NormalizeConfigLines(sshOutput, platform);
        var missingKeys = storedSet.Except(deviceSet).ToHashSet();

        if (missingKeys.Count == 0)
            return Json(new { success = true, applied = false, output = $"✔ Already in sync — nothing to apply on {configType}." });

        var originalLines = OriginalCaseConfigLines(config.Config1, platform);
        var linesToApply = missingKeys
            .Select(key => originalLines.TryGetValue(key, out var orig) ? orig : key)
            .ToList();

        _logger.LogInformation("ApplyToDevice: {Device} ({Host}) applying {Count} lines by {User}",
            configType, device.MgmtIpv4, linesToApply.Count, GetUserEmail());

        var (applySuccess, transcript, compareOutput) =
            await _sshService.RunConfigSessionAsync(device.MgmtIpv4, 22, linesToApply, platform);

        if (!applySuccess)
        {
            _logger.LogWarning("ApplyToDevice: {Device} config session failed", configType);
            var applyRun = await RecordDeviceApplyRunAsync(tenantGuid, configType, false, "Apply Failed", transcript);
            return Json(new { success = false, output = transcript, applyRun });
        }

        _logger.LogInformation("ApplyToDevice: {Device} successfully applied {Count} lines", configType, linesToApply.Count);

        var successRun = await RecordDeviceApplyRunAsync(tenantGuid, configType, true, "Applied", transcript);

        return Json(new
        {
            success = true,
            applied = true,
            deviceName = configType,
            appliedCount = linesToApply.Count,
            appliedLines = linesToApply,
            compareOutput,
            transcript,
            applyRun = successRun
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PatchDevice(Guid tenantGuid, string configType)
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
            return Json(new { success = false, output = $"Device '{configType}' not found or has no management IP." });

        var searchPattern = $"Cust{tenant.TenantId}";
        var platform = PlatformDetector.DetectPlatform(configType);

        var showCommand = PlatformDetector.GetShowCommand(platform, searchPattern);
        if (showCommand == null)
            return Json(new { success = false, output = $"Unknown platform for device '{configType}'." });

        var (sshSuccess, sshOutput) = await _sshService.RunCommandAsync(device.MgmtIpv4, 22, showCommand);
        if (!sshSuccess)
            return Json(new { success = false, output = $"SSH failed during comparison: {sshOutput}" });

        var storedSet = NormalizeConfigLines(config.Config1, platform);
        var deviceSet = NormalizeConfigLines(sshOutput, platform);
        var missingKeys = storedSet.Except(deviceSet).ToHashSet();
        var extraKeys = deviceSet.Except(storedSet).ToHashSet();

        if (missingKeys.Count == 0 && extraKeys.Count == 0)
            return Json(new { success = true, patched = false, output = $"✔ Already in sync — nothing to patch on {configType}." });

        var storedOriginals = OriginalCaseConfigLines(config.Config1, platform);
        var deviceOriginals = OriginalCaseConfigLines(sshOutput, platform);
        var configLines = new List<string>();
        var addedLines = new List<string>();
        var removedLines = new List<string>();

        foreach (var key in missingKeys)
        {
            var line = storedOriginals.TryGetValue(key, out var orig) ? orig : key;
            configLines.Add(line);
            addedLines.Add(line);
        }

        var removePrefix = platform == "Juniper" ? "delete " : "no ";
        foreach (var key in extraKeys)
        {
            var line = deviceOriginals.TryGetValue(key, out var orig) ? orig : key;
            if (platform == "Juniper" && line.StartsWith("set ", StringComparison.OrdinalIgnoreCase))
                line = line[4..];
            var removeLine = removePrefix + line;
            configLines.Add(removeLine);
            removedLines.Add(removeLine);
        }

        _logger.LogInformation("PatchDevice: {Device} ({Host}) patching +{AddCount}/-{RemoveCount} lines by {User}",
            configType, device.MgmtIpv4, addedLines.Count, removedLines.Count, GetUserEmail());

        var (patchSuccess, transcript, compareOutput) =
            await _sshService.RunConfigSessionAsync(device.MgmtIpv4, 22, configLines, platform);

        if (!patchSuccess)
        {
            _logger.LogWarning("PatchDevice: {Device} config session failed", configType);
            return Json(new { success = false, output = transcript });
        }

        _logger.LogInformation("PatchDevice: {Device} successfully patched +{AddCount}/-{RemoveCount} lines",
            configType, addedLines.Count, removedLines.Count);

        return Json(new
        {
            success = true,
            patched = true,
            deviceName = configType,
            addedLines,
            removedLines,
            addedCount = addedLines.Count,
            removedCount = removedLines.Count,
            compareOutput,
            transcript
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

        _logger.LogInformation("RemoveFromDevice: {Device} ({Host}) removing {Count} lines by {User}",
            configType, device.MgmtIpv4, lines.Count, GetUserEmail());

        var (removeSuccess, transcript, compareOutput) =
            await _sshService.RunConfigSessionAsync(device.MgmtIpv4, 22, lines, platform);

        if (!removeSuccess)
        {
            _logger.LogWarning("RemoveFromDevice: {Device} config session failed", configType);
            return Json(new { success = false, output = transcript });
        }

        _logger.LogInformation("RemoveFromDevice: {Device} successfully removed {Count} lines", configType, lines.Count);

        return Json(new
        {
            success = true,
            removed = true,
            deviceName = configType,
            removedLines = lines,
            removedCount = lines.Count,
            compareOutput,
            transcript
        });
    }

    private static HashSet<string> NormalizeConfigLines(string text, string platform)
    {
        var commentPrefixes = platform == "Juniper"
            ? new[] { "#", "##" }
            : new[] { "!" };

        return text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !commentPrefixes.Any(p => line.StartsWith(p)))
            .Select(line => line.ToLowerInvariant())
            .ToHashSet();
    }

    private static Dictionary<string, string> OriginalCaseConfigLines(string text, string platform)
    {
        var commentPrefixes = platform == "Juniper"
            ? new[] { "#", "##" }
            : new[] { "!" };

        var result = new Dictionary<string, string>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (commentPrefixes.Any(p => line.StartsWith(p)))
                continue;
            var key = line.ToLowerInvariant();
            result.TryAdd(key, line);
        }
        return result;
    }

    private async Task<object> RecordDeviceApplyRunAsync(Guid tenantGuid, string configType, bool success, string status, string output)
    {
        var run = new DeviceActionRun
        {
            DeviceActionRunId = Guid.NewGuid(),
            TenantGuid = tenantGuid,
            ConfigType = configType,
            ActionType = "Apply",
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
