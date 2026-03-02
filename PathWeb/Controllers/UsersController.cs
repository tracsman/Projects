using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class UsersController : Controller
{
    private readonly LabConfigContext _context;
    private readonly ILogger<UsersController> _logger;

    public UsersController(LabConfigContext context, ILogger<UsersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private byte GetAuthLevel() => (byte)(HttpContext.Items["AuthLevel"] ?? (byte)0);
    private string GetUserEmail() => User.Identity?.Name ?? "unknown";

    public async Task<IActionResult> Index()
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdminReadOnly)
        {
            _logger.LogWarning("Permission denied for Users.Index, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        _logger.LogInformation("Users.Index requested by {User}", GetUserEmail());
        var users = await _context.Users.OrderBy(u => u.UserName).ToListAsync();
        return View(users);
    }

    public async Task<IActionResult> Details(Guid? id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdminReadOnly)
        {
            _logger.LogWarning("Permission denied for Users.Details, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id == null)
        {
            _logger.LogWarning("Users.Details called with missing ID by {User}", GetUserEmail());
            TempData["Message"] = "Request missing required User GUID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id);
        if (user == null)
        {
            _logger.LogWarning("Users.Details called with invalid ID {UserId} by {User}", id, GetUserEmail());
            TempData["Message"] = "Invalid User GUID requested!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        _logger.LogInformation("Users.Details for {TargetUser} by {User}", user.UserName, GetUserEmail());
        return View(user);
    }

    public IActionResult Create()
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for Users.Create, user: {User}", GetUserEmail());
            return View("PermissionError");
        }
        _logger.LogInformation("Users.Create page requested by {User}", GetUserEmail());
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(User user)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for Users.Create [POST], user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (ModelState.IsValid)
        {
            user.UserId = Guid.NewGuid();
            _context.Users.Add(user);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UQ_Users_UserName") == true)
            {
                _logger.LogWarning("Duplicate UserName attempted: {UserName} by {User}", user.UserName, GetUserEmail());
                ModelState.AddModelError("UserName", "This email address is already in use.");
                return View(user);
            }
            _logger.LogInformation("User created: {UserName} (AuthLevel: {AuthLevel}) by {User}", user.UserName, user.AuthLevel, GetUserEmail());
            TempData["Message"] = "User was successfully created!";
            TempData["MessageLevel"] = "success";
            return RedirectToAction(nameof(Index));
        }
        return View(user);
    }

    public async Task<IActionResult> Edit(Guid? id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for Users.Edit, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id == null)
        {
            _logger.LogWarning("Users.Edit called with missing ID by {User}", GetUserEmail());
            TempData["Message"] = "Request missing required User GUID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            _logger.LogWarning("Users.Edit called with invalid ID {UserId} by {User}", id, GetUserEmail());
            TempData["Message"] = "Invalid User GUID requested!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        _logger.LogInformation("Users.Edit page for {TargetUser} by {User}", user.UserName, GetUserEmail());
        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Guid id, User user)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for Users.Edit [POST], user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id != user.UserId)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(user);
                await _context.SaveChangesAsync();
                _logger.LogInformation("User updated: {UserName} (AuthLevel: {AuthLevel}) by {User}", user.UserName, user.AuthLevel, GetUserEmail());
                TempData["Message"] = "User was successfully updated!";
                TempData["MessageLevel"] = "success";
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UQ_Users_UserName") == true)
            {
                _logger.LogWarning("Duplicate UserName on edit attempted: {UserName} by {User}", user.UserName, GetUserEmail());
                ModelState.AddModelError("UserName", "This email address is already in use.");
                return View(user);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Users.AnyAsync(u => u.UserId == user.UserId))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        return View(user);
    }

    public async Task<IActionResult> Delete(Guid? id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for Users.Delete, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (id == null)
        {
            _logger.LogWarning("Users.Delete called with missing ID by {User}", GetUserEmail());
            TempData["Message"] = "Request missing required User GUID!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == id);
        if (user == null)
        {
            _logger.LogWarning("Users.Delete called with invalid ID {UserId} by {User}", id, GetUserEmail());
            TempData["Message"] = "Invalid User GUID requested!";
            TempData["MessageLevel"] = "danger";
            return RedirectToAction(nameof(Index));
        }

        _logger.LogInformation("Users.Delete confirmation page for {TargetUser} by {User}", user.UserName, GetUserEmail());
        return View(user);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(Guid id)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for Users.Delete [POST], user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var user = await _context.Users.FindAsync(id);
        if (user != null)
        {
            _logger.LogInformation("User deleted: {UserName} by {User}", user.UserName, GetUserEmail());
            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            TempData["Message"] = "User was removed from the server!";
            TempData["MessageLevel"] = "success";
        }
        return RedirectToAction(nameof(Index));
    }
}
