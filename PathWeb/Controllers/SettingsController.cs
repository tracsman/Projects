using Microsoft.AspNetCore.Mvc;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class SettingsController : BaseController
{
    private readonly SettingsService _settingsService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(SettingsService settingsService, ILogger<SettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdminReadOnly)
        {
            _logger.LogWarning("Permission denied for Settings.Index, user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        var model = new SettingsViewModel
        {
            AutoDeleteRunbooks = await _settingsService.GetAutoDeleteRunbooksAsync(cancellationToken),
            AutomationRunbookType = await _settingsService.GetAutomationRunbookTypeAsync(cancellationToken),
            LoggingDefaultLevel = (await _settingsService.GetLoggingSettingsAsync(cancellationToken)).DefaultLevel,
            LoggingOverrides = (await _settingsService.GetLoggingSettingsAsync(cancellationToken)).CategoryLevels
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new LoggingOverrideViewModel { Category = kvp.Key, Level = kvp.Value })
                .ToList(),
            CanEdit = GetAuthLevel() >= (byte)AuthLevels.SiteAdmin
        };

        _logger.LogInformation("Settings.Index requested by {User}", GetUserEmail());
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SettingsViewModel model, CancellationToken cancellationToken)
    {
        if (GetAuthLevel() < (byte)AuthLevels.SiteAdmin)
        {
            _logger.LogWarning("Permission denied for Settings.Index [POST], user: {User}", GetUserEmail());
            return View("PermissionError");
        }

        if (string.IsNullOrWhiteSpace(model.AutomationRunbookType))
        {
            ModelState.AddModelError(nameof(model.AutomationRunbookType), "Automation Runbook Type is required.");
        }

        if (string.IsNullOrWhiteSpace(model.LoggingDefaultLevel))
            ModelState.AddModelError(nameof(model.LoggingDefaultLevel), "Default log level is required.");

        for (var i = 0; i < model.LoggingOverrides.Count; i++)
        {
            var overrideItem = model.LoggingOverrides[i];
            if (!string.IsNullOrWhiteSpace(overrideItem.Level) && string.IsNullOrWhiteSpace(overrideItem.Category))
                ModelState.AddModelError($"LoggingOverrides[{i}].Category", "Category is required when a log level override is provided.");
        }

        if (!ModelState.IsValid)
        {
            model.CanEdit = true;
            return View(model);
        }

        await _settingsService.SaveAutoDeleteRunbooksAsync(model.AutoDeleteRunbooks, cancellationToken);
        await _settingsService.SaveAutomationRunbookTypeAsync(model.AutomationRunbookType, cancellationToken);
        await _settingsService.SaveLoggingSettingsAsync(
            model.LoggingDefaultLevel,
            model.LoggingOverrides
                .Where(o => !string.IsNullOrWhiteSpace(o.Category))
                .Select(o => new KeyValuePair<string, string>(o.Category.Trim(), o.Level)),
            cancellationToken);

        _logger.LogInformation("Settings updated by {User}: AutoDeleteRunbook={Enabled}, AutomationRunbookType={RunbookType}, LoggingDefault={LoggingDefault}, LoggingOverrides={LoggingOverrideCount}",
            GetUserEmail(), model.AutoDeleteRunbooks, model.AutomationRunbookType, model.LoggingDefaultLevel, model.LoggingOverrides.Count(o => !string.IsNullOrWhiteSpace(o.Category)));
        TempData["Message"] = "Settings were successfully updated!";
        TempData["MessageLevel"] = "success";
        return RedirectToAction(nameof(Index));
    }
}
