using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class DevicesController : BaseController
{
    private readonly LabConfigContext _context;
    private readonly SshService _sshService;
    private readonly ILogger<DevicesController> _logger;

    private static readonly string[] NetworkDeviceTypes = ["Router", "Firewall", "Switch", "Access-In"];

    public DevicesController(LabConfigContext context, SshService sshService, ILogger<DevicesController> logger)
    {
        _context = context;
        _sshService = sshService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? sortOrder)
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdminReadOnly)
        {
            _logger.LogWarning("Permission denied for Devices.Index, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        ViewData["LabSortParm"] = sortOrder == "Lab" ? "Lab_desc" : "Lab";
        ViewData["NameSortParm"] = sortOrder == "Name" ? "Name_desc" : "Name";
        ViewData["TypeSortParm"] = sortOrder == "Type" ? "Type_desc" : "Type";
        ViewData["OsSortParm"] = sortOrder == "OS" ? "OS_desc" : "OS";
        ViewData["InServiceSortParm"] = sortOrder == "InService" ? "InService_desc" : "InService";

        var devices = _context.Devices.AsQueryable();

        devices = sortOrder switch
        {
            "Lab_desc" => devices.OrderByDescending(d => d.Lab).ThenByDescending(d => d.Name),
            "Name" => devices.OrderBy(d => d.Name),
            "Name_desc" => devices.OrderByDescending(d => d.Name),
            "Type" => devices.OrderBy(d => d.Type).ThenBy(d => d.Lab).ThenBy(d => d.Name),
            "Type_desc" => devices.OrderByDescending(d => d.Type).ThenByDescending(d => d.Lab).ThenByDescending(d => d.Name),
            "OS" => devices.OrderBy(d => d.Os).ThenBy(d => d.Lab).ThenBy(d => d.Name),
            "OS_desc" => devices.OrderByDescending(d => d.Os).ThenByDescending(d => d.Lab).ThenByDescending(d => d.Name),
            "InService" => devices.OrderBy(d => d.InService).ThenBy(d => d.Lab).ThenBy(d => d.Name),
            "InService_desc" => devices.OrderByDescending(d => d.InService).ThenByDescending(d => d.Lab).ThenByDescending(d => d.Name),
            _ => devices.OrderBy(d => d.Lab).ThenBy(d => d.Name),
        };

        _logger.LogInformation("Devices.Index requested by {User}, sort: {Sort}", GetUserEmail(), sortOrder ?? "default");
        return View(await devices.ToListAsync());
    }

    public async Task<IActionResult> Details(Guid? id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdminReadOnly)
        {
            _logger.LogWarning("Permission denied for Devices.Details, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id == null)
        {
            _logger.LogWarning("Devices.Details called with missing ID by {User}", GetUserEmail());
            TempData["Message"] = "Request missing required Device ID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        var device = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceId == id);
        if (device == null)
        {
            _logger.LogWarning("Devices.Details called with invalid ID {DeviceId} by {User}", id, GetUserEmail());
            TempData["Message"] = "Invalid Device ID requested!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        _logger.LogInformation("Devices.Details for {DeviceName} by {User}", device.Name, GetUserEmail());
        return View(device);
    }

    public async Task<IActionResult> Edit(Guid? id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
        {
            _logger.LogWarning("Permission denied for Devices.Edit, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id == null)
        {
            _logger.LogWarning("Devices.Edit called with missing ID by {User}", GetUserEmail());
            TempData["Message"] = "Request missing required Device ID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        var device = await _context.Devices.FindAsync(id);
        if (device == null)
        {
            _logger.LogWarning("Devices.Edit called with invalid ID {DeviceId} by {User}", id, GetUserEmail());
            TempData["Message"] = "Invalid Device ID requested!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        _logger.LogInformation("Devices.Edit page for {DeviceName} by {User}", device.Name, GetUserEmail());
        return View(device);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, Device device)
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
        {
            _logger.LogWarning("Permission denied for Devices.Edit [POST], user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id != device.DeviceId)
            return NotFound();

        var existingDevice = await _context.Devices.FirstOrDefaultAsync(d => d.DeviceId == id);
        if (existingDevice == null)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                existingDevice.MgmtIpv4 = device.MgmtIpv4;
                existingDevice.MgmtIpv6 = device.MgmtIpv6;
                existingDevice.Os = device.Os;
                existingDevice.InService = device.InService;
                existingDevice.Issues = device.Issues;

                await _context.SaveChangesAsync();
                _logger.LogInformation("Device updated: {DeviceName} by {User}", existingDevice.Name, GetUserEmail());
                TempData["Message"] = $"Device {existingDevice.Name} was successfully updated!";
                TempData["MessageLevel"] = "success";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Devices.AnyAsync(d => d.DeviceId == device.DeviceId))
                    return NotFound();
                throw;
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Database update failed while editing device {DeviceName} by {User}", existingDevice.Name, GetUserEmail());
                ModelState.AddModelError(string.Empty, "Save failed. Check field lengths and values, then try again. OS must be 50 characters or fewer.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while editing device {DeviceName} by {User}", existingDevice.Name, GetUserEmail());
                ModelState.AddModelError(string.Empty, "An unexpected error occurred while saving the device.");
            }

            if (ModelState.IsValid)
                return RedirectToAction(nameof(Index));

            device.Name = existingDevice.Name;
            device.Type = existingDevice.Type;
            device.Lab = existingDevice.Lab;
            return View(device);
        }

        _logger.LogWarning("Devices.Edit [POST] model validation failed for {DeviceName} by {User}", existingDevice.Name, GetUserEmail());
        return View(device);
    }

    public async Task<IActionResult> RunCommand()
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
        {
            _logger.LogWarning("Permission denied for Devices.RunCommand, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        ViewBag.Devices = await GetNetworkDevices();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunCommand(Guid deviceId, string command = "show version")
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
        {
            _logger.LogWarning("Permission denied for Devices.RunCommand [POST], user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var device = await _context.Devices.FindAsync(deviceId);
        if (device is null || string.IsNullOrEmpty(device.MgmtIpv4))
        {
            TempData["Message"] = "Device not found or has no management IP.";
            TempData["MessageLevel"] = "danger";
            ViewBag.Devices = await GetNetworkDevices();
            return View();
        }

        _logger.LogInformation("RunCommand on {Device} ({Host}): {Command} by {User}",
            device.Name, device.MgmtIpv4, command, GetUserEmail());

        var (success, output) = await _sshService.RunCommandAsync(device.MgmtIpv4, 22, command);

        ViewBag.Devices = await GetNetworkDevices();
        ViewBag.SelectedDeviceId = deviceId;
        ViewBag.Command = command;
        ViewBag.DeviceName = device.Name;
        ViewBag.DeviceIp = device.MgmtIpv4;
        ViewBag.Success = success;
        ViewBag.Output = output;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PopulateOs()
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
        {
            _logger.LogWarning("Permission denied for Devices.PopulateOs, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var devices = await _context.Devices
            .Where(d => NetworkDeviceTypes.Contains(d.Type) && d.InService && d.MgmtIpv4 != null)
            .OrderBy(d => d.Name)
            .ToListAsync();

        int updated = 0, failed = 0;
        var errors = new List<string>();

        foreach (var device in devices)
        {
            var (success, osVersion) = await _sshService.DetectOsVersionAsync(device.MgmtIpv4!, device.Name);
            if (success)
            {
                device.Os = osVersion.Length > 30 ? osVersion[..30] : osVersion;
                updated++;
            }
            else
            {
                errors.Add($"{device.Name}: {osVersion}");
                failed++;
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("PopulateOs completed: {Updated} updated, {Failed} failed, by {User}",
            updated, failed, GetUserEmail());

        if (failed == 0)
        {
            TempData["Message"] = $"OS versions updated for all {updated} devices.";
            TempData["MessageLevel"] = "success";
        }
        else
        {
            TempData["Message"] = $"OS versions updated for {updated} devices. {failed} failed: {string.Join("; ", errors)}";
            TempData["MessageLevel"] = "warning";
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<List<Device>> GetNetworkDevices()
    {
        return await _context.Devices
            .Where(d => NetworkDeviceTypes.Contains(d.Type) && d.InService && d.MgmtIpv4 != null)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }

    private static readonly string[] ServerTypes = ["Server"];
    private const string ServerAdminVaultName = "LabSecrets";
    private const string ServerAdminSecretName = "Server-Admin";
    private const string ServerAdminUserName = "Administrator";

    /// <summary>
    /// Validates SSH connectivity and readiness for a device.
    /// Network devices: runs "show version" with device credentials.
    /// Servers: runs a multi-check script via pwsh with server admin credentials:
    ///   SSH connectivity, PowerShell 7, Windows Server 2025, sshd running, LabMod module, log directory.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Validate(Guid id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdminReadOnly)
            return Json(new { success = false, error = "Permission denied." });

        var device = await _context.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceId == id);
        if (device == null)
            return Json(new { success = false, error = "Device not found." });

        if (string.IsNullOrWhiteSpace(device.MgmtIpv4))
            return Json(new { success = false, error = "Device has no management IPv4 address.", device = device.Name });

        var isServer = ServerTypes.Contains(device.Type) || device.Name.Contains("-ER-", StringComparison.OrdinalIgnoreCase);

        _logger.LogInformation("Device.Validate for {DeviceName} ({Host}), isServer={IsServer}, by {User}",
            device.Name, device.MgmtIpv4, isServer, GetUserEmail());

        bool success;
        string output;

        if (isServer)
        {
            try
            {
                var secretClient = new SecretClient(
                    new Uri($"https://{ServerAdminVaultName.ToLowerInvariant()}.vault.azure.net/"),
                    new DefaultAzureCredential());
                var secret = await secretClient.GetSecretAsync(ServerAdminSecretName);

                var script = """
                    $checks = @()
                    # PowerShell 7
                    $pwshOk = $PSVersionTable.PSVersion.Major -ge 7
                    $checks += [pscustomobject]@{ check='PowerShell 7'; pass=$pwshOk; detail="v$($PSVersionTable.PSVersion)" }
                    # Windows Server 2025
                    $os = (Get-CimInstance Win32_OperatingSystem).Caption
                    $osOk = $os -match '2025'
                    $checks += [pscustomobject]@{ check='Windows Server 2025'; pass=[bool]$osOk; detail=$os }
                    # sshd running
                    $sshd = Get-Service sshd -ErrorAction SilentlyContinue
                    $sshdOk = $sshd -and $sshd.Status -eq 'Running'
                    $checks += [pscustomobject]@{ check='SSH Server (sshd)'; pass=[bool]$sshdOk; detail=if($sshd){"Status: $($sshd.Status)"}else{"Not installed"} }
                    # LabMod module
                    $mod = Get-Module -ListAvailable -Name LabMod -ErrorAction SilentlyContinue | Sort-Object Version -Descending | Select-Object -First 1
                    $modOk = $mod -and $mod.Version -ge [version]'1.5.0.0'
                    $checks += [pscustomobject]@{ check='LabMod module (>= 1.5.0)'; pass=[bool]$modOk; detail=if($mod){"v$($mod.Version) at $($mod.ModuleBase)"}else{"Not found"} }
                    # Log directory
                    $logOk = Test-Path 'C:\Hyper-V\Logs'
                    $checks += [pscustomobject]@{ check='Log directory (C:\Hyper-V\Logs)'; pass=$logOk; detail=if($logOk){"Exists"}else{"Missing"} }
                    $checks | ConvertTo-Json -Compress -Depth 3
                    """;
                var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                var command = $"pwsh -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";

                (success, output) = await _sshService.RunCommandWithCredentialsAsync(
                    device.MgmtIpv4, 22, ServerAdminUserName, secret.Value.Value, command);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Device.Validate credential retrieval failed for {DeviceName}", device.Name);
                return Json(new { success = false, error = $"Failed to retrieve server credentials: {ex.Message}", device = device.Name });
            }
        }
        else
        {
            var platform = PlatformDetector.DetectPlatform(device.Name);
            var command = platform == "Juniper" ? "show version brief" : "show version";
            (success, output) = await _sshService.RunCommandAsync(device.MgmtIpv4, 22, command);
        }

        _logger.LogInformation("Device.Validate result for {DeviceName}: {Success}", device.Name, success);

        // For servers, parse the structured check results and determine overall pass/fail
        if (isServer && success)
        {
            try
            {
                var checks = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(output.Trim());
                var items = checks.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? checks.EnumerateArray().ToList()
                    : [checks];

                var allPassed = items.All(c => c.TryGetProperty("pass", out var p) && p.GetBoolean());
                return Json(new
                {
                    success = true,
                    allPassed,
                    device = device.Name,
                    host = device.MgmtIpv4,
                    isServer,
                    checks = items.Select(c => new
                    {
                        check = c.TryGetProperty("check", out var ch) ? ch.GetString() : "Unknown",
                        pass = c.TryGetProperty("pass", out var p) && p.GetBoolean(),
                        detail = c.TryGetProperty("detail", out var d) ? d.GetString() : ""
                    })
                });
            }
            catch
            {
                // JSON parse failed — fall through to raw output
            }
        }

        return Json(new
        {
            success,
            device = device.Name,
            host = device.MgmtIpv4,
            isServer,
            output = output.Length > 2000 ? output[..2000] : output
        });
    }
}
