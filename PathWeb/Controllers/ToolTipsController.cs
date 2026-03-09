using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class ToolTipsController : Controller
{
    private readonly LabConfigContext _context;
    private readonly ILogger<ToolTipsController> _logger;
    private readonly IMemoryCache _cache;

    public ToolTipsController(LabConfigContext context, ILogger<ToolTipsController> logger, IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
    }

    private byte GetAuthLevel() => (byte)(HttpContext.Items["AuthLevel"] ?? (byte)0);
    private string GetUserEmail() => User.Identity?.Name ?? "unknown";

    public async Task<IActionResult> Index()
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for ToolTips.Index, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        _logger.LogInformation("ToolTips.Index requested by {User}", GetUserEmail());
        var tips = await _context.FieldHelps.OrderBy(f => f.FieldName).ToListAsync();
        return View(tips);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Dictionary<string, string> helpTexts)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for ToolTips.Save, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var tips = await _context.FieldHelps.ToListAsync();
        var updated = 0;

        foreach (var tip in tips)
        {
            if (helpTexts.TryGetValue(tip.FieldName, out var newText) && newText != tip.HelpText)
            {
                tip.HelpText = newText;
                updated++;
            }
        }

        if (updated > 0)
        {
            await _context.SaveChangesAsync();
            _cache.Remove("FieldHelpDictionary");
            _logger.LogInformation("ToolTips updated: {Count} row(s) changed by {User}", updated, GetUserEmail());
            TempData["Message"] = $"{updated} tooltip(s) updated successfully!";
            TempData["MessageLevel"] = "success";
        }
        else
        {
            TempData["Message"] = "No changes detected.";
            TempData["MessageLevel"] = "info";
        }

        return RedirectToAction(nameof(Index));
    }
}
