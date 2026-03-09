using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class SshTestController : Controller
{
    private readonly SshService _sshService;
    private readonly LabConfigContext _context;
    private readonly ILogger<SshTestController> _logger;

    private static readonly string[] DeviceTypes = ["Router", "Firewall", "Switch"];

    public SshTestController(SshService sshService, LabConfigContext context, ILogger<SshTestController> logger)
    {
        _sshService = sshService;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// GET /SshTest — shows a device dropdown and command field to test SSH connectivity.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var devices = await _context.Devices
            .Where(d => DeviceTypes.Contains(d.Type) && d.InService && d.MgmtIpv4 != null)
            .OrderBy(d => d.Name)
            .Select(d => new { d.DeviceId, d.Name, d.Type, d.MgmtIpv4 })
            .ToListAsync();

        var options = string.Join("\n",
            devices.Select(d => $"<option value=\"{d.DeviceId}\">{d.Name} ({d.Type}) — {d.MgmtIpv4}</option>"));

        var html = $"""
            <!DOCTYPE html>
            <html><head><title>SSH Test</title></head>
            <body style="font-family:monospace;max-width:600px;margin:40px auto">
            <h2>SSH Connectivity Test</h2>
            <p>Credentials are loaded from Key Vault.</p>
            <form method="post">
                <label>Device:<br/>
                <select name="deviceId" style="width:100%">
                    {options}
                </select></label><br/><br/>
                <label>Command: <input name="command" value="show version" style="width:300px" /></label><br/><br/>
                <button type="submit">Test Connection</button>
            </form>
            </body></html>
            """;

        return Content(html, "text/html; charset=utf-8");
    }

    /// <summary>
    /// POST /SshTest — looks up the device, fetches credentials from Key Vault, and runs the SSH command.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Index(Guid deviceId, string command = "show version")
    {
        var device = await _context.Devices.FindAsync(deviceId);
        if (device is null || string.IsNullOrEmpty(device.MgmtIpv4))
            return Content("Device not found or has no management IP.", "text/plain; charset=utf-8");

        _logger.LogInformation("SSH test requested for {Device} ({Host}) by {User}",
            device.Name, device.MgmtIpv4, User.Identity?.Name ?? "unknown");

        var (success, output) = await _sshService.RunCommandAsync(device.MgmtIpv4, 22, command);

        var header = success
            ? $"✅ SUCCESS — {device.Name} ({device.MgmtIpv4})"
            : $"❌ FAILED — {device.Name} ({device.MgmtIpv4})";

        return Content($"{header}\n\n{output}", "text/plain; charset=utf-8");
    }

    /// <summary>
    /// GET /SshTest/PopulateOs — SSHes into each device, detects the OS version, and updates the database.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PopulateOs()
    {
        var devices = await _context.Devices
            .Where(d => DeviceTypes.Contains(d.Type) && d.InService && d.MgmtIpv4 != null)
            .OrderBy(d => d.Name)
            .ToListAsync();

        var results = new List<string> { $"Populating OS for {devices.Count} devices...\n" };

        foreach (var device in devices)
        {
            var (success, osVersion) = await _sshService.DetectOsVersionAsync(device.MgmtIpv4!);

            if (success)
            {
                var oldOs = device.Os ?? "(empty)";
                device.Os = osVersion.Length > 30 ? osVersion[..30] : osVersion;
                results.Add($"✅ {device.Name,-22} {oldOs} → {device.Os}");
            }
            else
            {
                results.Add($"❌ {device.Name,-22} {osVersion}");
            }
        }

        await _context.SaveChangesAsync();
        results.Add($"\nDone — database updated.");

        _logger.LogInformation("PopulateOs completed for {Count} devices by {User}",
            devices.Count, User.Identity?.Name ?? "unknown");

        return Content(string.Join("\n", results), "text/plain; charset=utf-8");
    }
}
