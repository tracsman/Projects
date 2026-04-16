using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Models;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class AutomationController : BaseController
{
    private static readonly HashSet<string> AllowedConfigTypes =
    [
        "CreateERPowerShell",
        "CreateAzurePowerShell",
        "ServiceProviderInstructions"
    ];

    private static readonly HashSet<string> TerminalStatuses =
    [
        "Completed",
        "Failed",
        "Stopped",
        "Suspended"
    ];

    private readonly LabConfigContext _context;
    private readonly AutomationService _automationService;
    private readonly ILogger<AutomationController> _logger;

    public AutomationController(
        LabConfigContext context,
        AutomationService automationService,
        ILogger<AutomationController> logger)
    {
        _context = context;
        _automationService = automationService;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] AutomationRunRequest request)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, output = "Permission denied." });

        if (request.TenantGuid == Guid.Empty || string.IsNullOrWhiteSpace(request.ConfigType))
            return Json(new { success = false, output = "Missing tenant or config type." });

        if (!AllowedConfigTypes.Contains(request.ConfigType))
            return Json(new { success = false, output = $"Config type '{request.ConfigType}' is not enabled for Azure Automation yet." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == request.TenantGuid);
        if (tenant == null)
            return Json(new { success = false, output = "Tenant not found." });

        if (string.Equals(request.ConfigType, "ServiceProviderInstructions", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(tenant.Ersku, "None", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, output = "No ExpressRoute circuit is configured for this tenant, so provider provisioning is not required." });

            if (!string.Equals(tenant.EruplinkPort, "ECX", StringComparison.OrdinalIgnoreCase))
                return Json(new { success = false, output = "This tenant uses ExpressRoute Direct, so provider provisioning is not required." });
        }

        var config = await _context.Configs.FirstOrDefaultAsync(c =>
            c.TenantGuid == request.TenantGuid &&
            c.ConfigVersion == tenant.ConfigVersion &&
            c.ConfigType == request.ConfigType);

        if (config == null || string.IsNullOrWhiteSpace(config.Config1))
            return Json(new { success = false, output = $"No stored config found for '{request.ConfigType}'." });

        var scriptContent = string.IsNullOrWhiteSpace(request.ScriptContent)
            ? config.Config1
            : request.ScriptContent;

        var result = await _automationService.SubmitPowerShellRunbookAsync(request.ConfigType, scriptContent, GetUserEmail());
        if (!result.Success)
        {
            _logger.LogWarning("Automation submit failed for {ConfigType}: {Error}", request.ConfigType, result.Error);
            return Json(new { success = false, output = result.Error ?? "Automation submit failed." });
        }

        var run = new AutomationRun
        {
            AutomationRunId = Guid.NewGuid(),
            TenantGuid = request.TenantGuid,
            ConfigType = request.ConfigType,
            JobId = result.JobId,
            RunbookName = result.RunbookName,
            SubmittedBy = GetUserEmail(),
            SubmittedDate = DateTime.UtcNow,
            Status = "Queued",
            LastStatusDate = DateTime.UtcNow,
            LastOutput = $"Automation runbook '{result.RunbookName}' submitted as job '{result.JobId}'.",
            PreparedScript = result.PreparedScript
        };

        _context.AutomationRuns.Add(run);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Automation submit queued for {ConfigType} as job {JobId} by {User}",
            request.ConfigType, result.JobId, GetUserEmail());

        return Json(new
        {
            success = true,
            run = ToClientModel(run),
            output = $"Automation runbook '{result.RunbookName}' submitted as job '{result.JobId}'."
        });
    }

    [HttpGet]
    public async Task<IActionResult> Status(string jobId)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            return Json(new { success = false, output = "Permission denied." });

        if (string.IsNullOrWhiteSpace(jobId))
            return Json(new { success = false, output = "Missing job ID." });

        var trackedRun = await _context.AutomationRuns.FirstOrDefaultAsync(r => r.JobId == jobId);
        if (trackedRun != null && (trackedRun.CompletedDate != null || IsTerminalStatus(trackedRun.Status)))
        {
            return Json(new
            {
                success = true,
                run = ToClientModel(trackedRun)
            });
        }

        var result = await _automationService.GetJobStatusAsync(jobId);
        if (!result.Success)
            return Json(new { success = false, output = result.Error ?? "Failed to read job status." });

        if (trackedRun != null)
        {
            var wasTerminal = trackedRun.CompletedDate != null || IsTerminalStatus(trackedRun.Status);
            ApplyJobStatus(trackedRun, result);
            await _context.SaveChangesAsync();

            if (!wasTerminal && result.IsTerminal)
                await TryCleanupRunbookAsync(trackedRun.RunbookName);
        }

        return Json(new
        {
            success = true,
            run = trackedRun != null
                ? ToClientModel(trackedRun)
                : new
                {
                    configType = string.Empty,
                    jobId = result.JobId,
                    runbookName = string.Empty,
                    status = result.Status,
                    isTerminal = result.IsTerminal,
                    output = result.Output,
                    exception = result.Exception,
                    preparedScript = string.Empty,
                    submittedBy = string.Empty,
                    submittedDate = (DateTime?)null,
                    lastStatusDate = (DateTime?)null,
                    completedDate = (DateTime?)null
                }
        });
    }

    [HttpGet]
    public async Task<IActionResult> Latest(Guid tenantGuid)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            return Json(new { success = false, output = "Permission denied." });

        if (tenantGuid == Guid.Empty)
            return Json(new { success = false, output = "Missing tenant GUID." });

        var latestRuns = await _context.AutomationRuns
            .Where(r => r.TenantGuid == tenantGuid)
            .OrderByDescending(r => r.SubmittedDate)
            .ToListAsync();

        var runsByConfig = latestRuns
            .GroupBy(r => r.ConfigType)
            .Select(g => g.First())
            .ToList();

        var changed = false;
        var runbooksToCleanup = new List<string>();
        foreach (var run in runsByConfig.Where(r => !IsTerminalStatus(r.Status)))
        {
            var statusResult = await _automationService.GetJobStatusAsync(run.JobId);
            if (!statusResult.Success)
            {
                _logger.LogWarning("Failed to refresh automation job {JobId} for latest run lookup: {Error}", run.JobId, statusResult.Error);
                continue;
            }

            var wasTerminal = run.CompletedDate != null || IsTerminalStatus(run.Status);
            ApplyJobStatus(run, statusResult);
            if (!wasTerminal && statusResult.IsTerminal && !string.IsNullOrWhiteSpace(run.RunbookName))
                runbooksToCleanup.Add(run.RunbookName);
            changed = true;
        }

        if (changed)
            await _context.SaveChangesAsync();

        foreach (var runbookName in runbooksToCleanup.Distinct(StringComparer.OrdinalIgnoreCase))
            await TryCleanupRunbookAsync(runbookName);

        return Json(new
        {
            success = true,
            runs = runsByConfig.Select(ToClientModel)
        });
    }

    public sealed class AutomationRunRequest
    {
        public Guid TenantGuid { get; set; }
        public string ConfigType { get; set; } = string.Empty;
        public string? ScriptContent { get; set; }
    }

    [HttpGet]
    public async Task<IActionResult> BackoutScript(Guid tenantGuid, string configType)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            return Json(new { success = false, error = "Permission denied." });

        if (tenantGuid == Guid.Empty || string.IsNullOrWhiteSpace(configType))
            return Json(new { success = false, error = "Missing tenant or config type." });

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid);
        if (tenant == null)
            return Json(new { success = false, error = "Tenant not found." });

        var backoutType = configType + "-out";
        var config = await _context.Configs.FirstOrDefaultAsync(c =>
            c.TenantGuid == tenantGuid &&
            c.ConfigVersion == tenant.ConfigVersion &&
            c.ConfigType == backoutType);

        if (config == null || string.IsNullOrWhiteSpace(config.Config1))
            return Json(new { success = false, error = $"No backout config found for '{configType}'." });

        return Json(new { success = true, script = config.Config1 });
    }

    private static void ApplyJobStatus(AutomationRun run, AutomationJobStatusResult result)
    {
        run.Status = result.Status;
        run.LastOutput = result.Output;
        run.LastException = result.Exception;
        run.LastStatusDate = DateTime.UtcNow;

        if (result.IsTerminal && run.CompletedDate == null)
            run.CompletedDate = DateTime.UtcNow;
    }

    private async Task TryCleanupRunbookAsync(string? runbookName)
    {
        if (string.IsNullOrWhiteSpace(runbookName))
            return;

        var deleted = await _automationService.DeleteRunbookAsync(runbookName);
        if (!deleted)
            _logger.LogWarning("Automation runbook cleanup did not complete for {RunbookName}", runbookName);
    }

    private static bool IsTerminalStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) && TerminalStatuses.Contains(status);

    private static object ToClientModel(AutomationRun run) => new
    {
        configType = run.ConfigType,
        jobId = run.JobId,
        runbookName = run.RunbookName,
        status = run.Status,
        isTerminal = run.CompletedDate != null || IsTerminalStatus(run.Status),
        output = run.LastOutput ?? string.Empty,
        exception = run.LastException ?? string.Empty,
        preparedScript = run.PreparedScript ?? string.Empty,
        submittedBy = run.SubmittedBy,
        submittedDate = run.SubmittedDate,
        lastStatusDate = run.LastStatusDate,
        completedDate = run.CompletedDate
    };
}
