using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class EmailController : BaseController
{
    private readonly LabConfigContext _context;
    private readonly LogicAppService _logicAppService;
    private readonly ILogger<EmailController> _logger;

    public EmailController(LabConfigContext context, LogicAppService logicAppService, ILogger<EmailController> logger)
    {
        _context = context;
        _logicAppService = logicAppService;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendNotification(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        if (!string.Equals(configType, "eMailHTML", StringComparison.Ordinal))
            return Json(new { success = false, output = $"Config type '{configType}' is not enabled for email sending." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        var config = await _context.Configs.FirstOrDefaultAsync(c =>
            c.TenantGuid == tenantGuid &&
            c.ConfigVersion == tenant.ConfigVersion &&
            c.ConfigType == configType);

        if (config == null || string.IsNullOrWhiteSpace(config.Config1))
            return Json(new { success = false, output = "No stored notification email found." });

        var htmlBody = config.Config1.Replace("cid:", "https://microsoft.sharepoint.com/teams/Pathfinders/SiteAssets/");
        var subject = "Your PathLab Environment is Ready!";

        var result = await _logicAppService.SendNotificationEmailAsync(tenant, subject, htmlBody, GetUserEmail());
        var status = result.Success ? "Sent" : "Send Failed";
        var output = result.Error ?? result.ResponseBody ?? string.Empty;

        var run = new EmailSendRun
        {
            EmailSendRunId = Guid.NewGuid(),
            TenantGuid = tenantGuid,
            ConfigType = configType,
            Success = result.Success,
            Status = status,
            SubmittedBy = GetUserEmail(),
            SubmittedDate = DateTime.UtcNow,
            Recipient = tenant.Contacts,
            Subject = subject,
            Output = string.IsNullOrWhiteSpace(output)
                ? (result.Success
                    ? $"Notification email sent to {tenant.Contacts}."
                    : "Notification email send failed.")
                : output
        };

        _context.EmailSendRuns.Add(run);
        await _context.SaveChangesAsync();

        if (result.Success)
        {
            _logger.LogInformation("Notification email sent for {TenantName} to {Recipient} by {User}",
                tenant.TenantName, tenant.Contacts, GetUserEmail());
            return Json(new
            {
                success = true,
                output = run.Output,
                emailRun = ToClientModel(run)
            });
        }

        _logger.LogWarning("Notification email send failed for {TenantName}: {Error}", tenant.TenantName, run.Output);
        return Json(new
        {
            success = false,
            output = run.Output,
            emailRun = ToClientModel(run)
        });
    }

    private static object ToClientModel(EmailSendRun run) => new
    {
        configType = run.ConfigType,
        success = run.Success,
        status = run.Status,
        submittedBy = run.SubmittedBy,
        submittedDate = run.SubmittedDate,
        recipient = run.Recipient ?? string.Empty,
        subject = run.Subject ?? string.Empty,
        output = run.Output ?? string.Empty
    };
}
