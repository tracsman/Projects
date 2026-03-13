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

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(device);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Device updated: {DeviceName} by {User}", device.Name, GetUserEmail());
                TempData["Message"] = $"Device {device.Name} was successfully updated!";
                TempData["MessageLevel"] = "success";
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Devices.AnyAsync(d => d.DeviceId == device.DeviceId))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
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
}
