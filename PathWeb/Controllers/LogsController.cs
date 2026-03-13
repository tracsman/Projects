using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class LogsController : Controller
{
    private readonly LabConfigContext _context;
    private readonly ILogger<LogsController> _logger;

    public LogsController(LabConfigContext context, ILogger<LogsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private byte GetAuthLevel() => (byte)(HttpContext.Items["AuthLevel"] ?? (byte)0);
    private string GetUserEmail() => User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                                     ?? User.Identity?.Name ?? "unknown";

    public async Task<IActionResult> Index(string? level, string? search, int page = 1)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdminReadOnly)
        {
            _logger.LogWarning("Permission denied for Logs.Index, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        const int pageSize = 50;

        var query = _context.AppLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(level))
            query = query.Where(l => l.Level == level);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(l => l.Message.Contains(search) || l.Category.Contains(search) || (l.UserName != null && l.UserName.Contains(search)));

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        page = Math.Clamp(page, 1, Math.Max(1, totalPages));

        var logs = await query
            .OrderByDescending(l => l.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        ViewData["CurrentLevel"] = level;
        ViewData["CurrentSearch"] = search;
        ViewData["CurrentPage"] = page;
        ViewData["TotalPages"] = totalPages;
        ViewData["TotalCount"] = totalCount;

        return View(logs);
    }
}
