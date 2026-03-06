using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class DevicesController : Controller
{
    private readonly LabConfigContext _context;
    private readonly ILogger<DevicesController> _logger;

    public DevicesController(LabConfigContext context, ILogger<DevicesController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private byte GetAuthLevel() => (byte)(HttpContext.Items["AuthLevel"] ?? (byte)0);
    private string GetUserEmail() => User.Identity?.Name ?? "unknown";

    public async Task<IActionResult> Index()
    {
        if (GetAuthLevel() < (byte)AuthLevels.DataAdminReadOnly)
        {
            _logger.LogWarning("Permission denied for Devices.Index, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        _logger.LogInformation("Devices.Index requested by {User}", GetUserEmail());
        var devices = await _context.Devices.OrderBy(d => d.Lab).ThenBy(d => d.Name).ToListAsync();
        return View(devices);
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
}
