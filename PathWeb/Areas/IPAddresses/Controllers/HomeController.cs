using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Areas.IPAddresses.Controllers
{
    [Area("IPAddresses")]
    public class HomeController : Controller
    {
        private readonly LabConfigContext _context;
        private readonly ILogger<HomeController> _logger;

        public HomeController(LabConfigContext context, ILogger<HomeController> logger)
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
                _logger.LogWarning("Permission denied for IPAddresses.Index, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            _logger.LogInformation("IPAddresses.Index requested by {User}", GetUserEmail());
            var publicIps = await _context.PublicIps.OrderBy(ip => ip.RangeId).ToListAsync();
            return View(publicIps);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.DataAdminReadOnly)
            {
                _logger.LogWarning("Permission denied for IPAddresses.Details, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                _logger.LogWarning("IPAddresses.Details called with missing ID by {User}", GetUserEmail());
                TempData["Message"] = "Request missing required Prefix ID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var publicIp = await _context.PublicIps.FirstOrDefaultAsync(ip => ip.RangeId == id);
            if (publicIp == null)
            {
                _logger.LogWarning("IPAddresses.Details called with invalid ID {RangeId} by {User}", id, GetUserEmail());
                TempData["Message"] = "Invalid Prefix ID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation("IPAddresses.Details for range {Range} by {User}", publicIp.Range, GetUserEmail());
            return View(publicIp);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
            {
                _logger.LogWarning("Permission denied for IPAddresses.Edit, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                _logger.LogWarning("IPAddresses.Edit called with missing ID by {User}", GetUserEmail());
                TempData["Message"] = "Request missing required Prefix ID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var publicIp = await _context.PublicIps.FindAsync(id);
            if (publicIp == null)
            {
                _logger.LogWarning("IPAddresses.Edit called with invalid ID {RangeId} by {User}", id, GetUserEmail());
                TempData["Message"] = "Invalid Prefix ID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            // Default AssignedBy to current user email if empty
            if (string.IsNullOrEmpty(publicIp.AssignedBy))
            {
                publicIp.AssignedBy = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                                      ?? User.Identity?.Name ?? "";
            }

            _logger.LogInformation("IPAddresses.Edit page for range {Range} by {User}", publicIp.Range, GetUserEmail());
            return View(publicIp);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PublicIp publicIp)
        {
            if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
            {
                _logger.LogWarning("Permission denied for IPAddresses.Edit [POST], user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id != publicIp.RangeId)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(publicIp);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("IP prefix updated: {Range} purpose: {Purpose} by {User}", publicIp.Range, publicIp.Purpose, GetUserEmail());
                    TempData["Message"] = "Address prefix was successfully updated!";
                    TempData["MessageLevel"] = "success";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await _context.PublicIps.AnyAsync(ip => ip.RangeId == publicIp.RangeId))
                        return NotFound();
                    throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(publicIp);
        }

        public async Task<IActionResult> Release(int? id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
            {
                _logger.LogWarning("Permission denied for IPAddresses.Release, user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            if (id == null)
            {
                _logger.LogWarning("IPAddresses.Release called with missing ID by {User}", GetUserEmail());
                TempData["Message"] = "Request missing required Prefix ID!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var publicIp = await _context.PublicIps.FirstOrDefaultAsync(ip => ip.RangeId == id);
            if (publicIp == null)
            {
                _logger.LogWarning("IPAddresses.Release called with invalid ID {RangeId} by {User}", id, GetUserEmail());
                TempData["Message"] = "Invalid Prefix ID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation("IPAddresses.Release confirmation for range {Range} by {User}", publicIp.Range, GetUserEmail());
            return View(publicIp);
        }

        [HttpPost, ActionName("Release")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReleaseConfirmed(int id)
        {
            if (GetAuthLevel() < (byte)AuthLevels.DataAdmin)
            {
                _logger.LogWarning("Permission denied for IPAddresses.Release [POST], user: {User}", GetUserEmail());
                return View("PermissionError");
            }

            var publicIp = await _context.PublicIps.FindAsync(id);
            if (publicIp == null)
            {
                _logger.LogWarning("IPAddresses.Release [POST] called with invalid ID {RangeId} by {User}", id, GetUserEmail());
                TempData["Message"] = "Invalid Prefix ID requested!";
                TempData["MessageLevel"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            _logger.LogInformation("IP prefix released: {Range} (was: {Purpose}) by {User}", publicIp.Range, publicIp.Purpose, GetUserEmail());
            publicIp.Device = null;
            publicIp.Purpose = null;
            publicIp.TenantGuid = null;
            publicIp.TenantId = null;
            publicIp.AssignedDate = null;
            publicIp.AssignedBy = null;

            await _context.SaveChangesAsync();
            TempData["Message"] = "Prefix assignment was released, and is now assignable again!";
            TempData["MessageLevel"] = "success";
            return RedirectToAction(nameof(Index));
        }
    }
}
