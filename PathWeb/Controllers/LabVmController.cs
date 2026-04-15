using System.Text;
using System.Text.Json;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PathWeb.Data;
using PathWeb.Services;

namespace PathWeb.Controllers;

public class LabVmController : BaseController
{
    private const string ConfigType = "LabVMPowerShell";
    private const string AdminVaultName = "LabSecrets";
    private const string AdminSecretName = "Server-Admin";
    private const string AdminUserName = "Administrator";
    private const string TenantUserSecretName = "PathLabUser";
    private const string TenantUserName = "PathLabUser";
    private const string JsonMarker = "---LABVM-JSON---";

    private readonly LabConfigContext _context;
    private readonly SshService _sshService;
    private readonly LabVmRunTracker _tracker;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LabVmController> _logger;

    public LabVmController(
        LabConfigContext context,
        SshService sshService,
        LabVmRunTracker tracker,
        IServiceScopeFactory scopeFactory,
        ILogger<LabVmController> logger)
    {
        _context = context;
        _sshService = sshService;
        _tracker = tracker;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>
    /// Returns the available Hyper-V servers for a lab plus the default server for a tenant.
    /// Called by the modal to populate the per-row server dropdowns.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Servers(Guid tenantGuid)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, error = "Permission denied." });

        var tenant = await _context.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid && t.DeletedDate == null);
        if (tenant == null)
            return Json(new { success = false, error = "Tenant not found." });

        var defaultServerName = GetDefaultServerName(tenant.Lab, tenant.TenantId);

        var servers = await _context.Devices.AsNoTracking()
            .Where(d => d.Lab == tenant.Lab && d.Name.Contains("-ER-") && d.InService && !string.IsNullOrEmpty(d.MgmtIpv4))
            .OrderBy(d => d.Name)
            .Select(d => new { d.Name, d.MgmtIpv4 })
            .ToListAsync();

        return Json(new { success = true, defaultServer = defaultServerName, servers });
    }

    /// <summary>
    /// Returns the VM requests parsed from the tenant's LabVMPowerShell config.
    /// Called by the modal to build the VM table rows.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Requests(Guid tenantGuid)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, error = "Permission denied." });

        var tenant = await _context.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantGuid == tenantGuid && t.DeletedDate == null);
        if (tenant == null)
            return Json(new { success = false, error = "Tenant not found." });

        var config = await _context.Configs.AsNoTracking()
            .Where(c => c.TenantGuid == tenantGuid && c.ConfigVersion == tenant.ConfigVersion && c.ConfigType == ConfigType)
            .OrderByDescending(c => c.CreatedDate)
            .FirstOrDefaultAsync();

        if (config == null || string.IsNullOrWhiteSpace(config.Config1))
            return Json(new { success = false, error = $"No {ConfigType} config found for this tenant." });

        var vmRequests = ParseVmRequests(config.Config1);
        if (vmRequests.Count == 0)
            return Json(new { success = false, error = "Config does not contain any New-LabVM commands." });

        var defaultServer = GetDefaultServerName(tenant.Lab, tenant.TenantId);

        return Json(new
        {
            success = true,
            tenant = new { tenant.TenantGuid, tenant.TenantId, tenant.TenantName, tenant.Lab },
            defaultServer,
            requests = vmRequests.Select(r => new { r.Index, r.Os })
        });
    }

    /// <summary>
    /// Launches Lab VM creation. Accepts per-VM server overrides.
    /// Returns immediately with a runId; poll Status for progress.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit([FromBody] LabVmSubmitRequest request)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantAdmin)
            return Json(new { success = false, error = "Permission denied." });

        if (request.TenantGuid == Guid.Empty)
            return Json(new { success = false, error = "Missing tenant." });

        var tenant = await _context.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantGuid == request.TenantGuid && t.DeletedDate == null);
        if (tenant == null)
            return Json(new { success = false, error = "Tenant not found." });

        var config = await _context.Configs.AsNoTracking()
            .Where(c => c.TenantGuid == tenant.TenantGuid && c.ConfigVersion == tenant.ConfigVersion && c.ConfigType == ConfigType)
            .OrderByDescending(c => c.CreatedDate)
            .FirstOrDefaultAsync();

        if (config == null || string.IsNullOrWhiteSpace(config.Config1))
            return Json(new { success = false, error = $"No {ConfigType} config found for this tenant." });

        var vmRequests = ParseVmRequests(config.Config1);
        if (vmRequests.Count == 0)
            return Json(new { success = false, error = "Config does not contain any New-LabVM commands." });

        // Apply per-VM server overrides from the request (or use default)
        var defaultServerName = GetDefaultServerName(tenant.Lab, tenant.TenantId);
        var overrides = request.VmOverrides ?? [];
        for (int i = 0; i < vmRequests.Count; i++)
        {
            var over = overrides.FirstOrDefault(o => o.Index == vmRequests[i].Index);
            vmRequests[i].TargetServer = !string.IsNullOrWhiteSpace(over?.TargetServer) ? over.TargetServer : defaultServerName;
        }

        // Resolve management IPs for all targeted servers
        var targetServerNames = vmRequests.Select(r => r.TargetServer).Distinct().ToList();
        var devices = await _context.Devices.AsNoTracking()
            .Where(d => targetServerNames.Contains(d.Name) && !string.IsNullOrEmpty(d.MgmtIpv4))
            .ToDictionaryAsync(d => d.Name, d => d.MgmtIpv4!);

        var missingServers = targetServerNames.Where(s => !devices.ContainsKey(s)).ToList();
        if (missingServers.Count > 0)
            return Json(new { success = false, error = $"Server(s) not found or missing management IP: {string.Join(", ", missingServers)}" });

        // Fetch credentials from Key Vault
        var tenantVaultName = $"{tenant.Lab}-Cust{tenant.TenantId}-kv";
        KeyVaultSecret adminSecret;
        KeyVaultSecret tenantUserSecret;
        try
        {
            adminSecret = (await CreateSecretClient(AdminVaultName).GetSecretAsync(AdminSecretName)).Value;
            tenantUserSecret = (await CreateSecretClient(tenantVaultName).GetSecretAsync(TenantUserSecretName)).Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lab VM secret retrieval failed for {TenantName}", tenant.TenantName);
            return Json(new { success = false, error = $"Failed to retrieve Key Vault secrets: {ex.Message}" });
        }

        var runId = $"labvm-{tenant.Lab.ToLowerInvariant()}-cust{tenant.TenantId}-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";

        // Build per-request tracking
        var requestInfos = vmRequests.Select(r => new LabVmRequestInfo
        {
            RequestId = $"{runId}-{r.Index:00}",
            Index = r.Index,
            Os = r.Os,
            TargetServer = r.TargetServer,
            TargetHost = devices[r.TargetServer]
        }).ToList();

        var run = new LabVmRunInfo
        {
            RunId = runId,
            TenantName = tenant.TenantName,
            TenantId = tenant.TenantId,
            Lab = tenant.Lab,
            SubmittedBy = GetUserEmail(),
            StartedAt = DateTimeOffset.UtcNow,
            Requests = requestInfos
        };
        _tracker.Track(runId, run);

        _logger.LogInformation("Lab VM run {RunId} submitted by {User} for {TenantName}: {Count} VM(s) across {ServerCount} server(s)",
            runId, GetUserEmail(), tenant.TenantName, requestInfos.Count, targetServerNames.Count);

        // Launch background execution — one SSH connection per unique server, in parallel
        var adminSecretValue = adminSecret.Value;
        var tenantSecretValue = tenantUserSecret.Value;
        var tenantGuid = tenant.TenantGuid;
        var submittedBy = GetUserEmail();

        _ = Task.Run(async () =>
        {
            try
            {
                // Group requests by target server
                var byServer = requestInfos.GroupBy(r => r.TargetServer).ToList();

                var serverTasks = byServer.Select(group => ExecuteServerBatchAsync(
                    run, group.ToList(), tenant.TenantId,
                    adminSecretValue, tenantSecretValue,
                    devices[group.Key]));

                await Task.WhenAll(serverTasks);

                // Aggregate final status
                var failed = requestInfos.Where(r => r.Status == "Failed").ToList();
                if (failed.Count > 0)
                {
                    run.Status = "Failed";
                    run.Error = $"{failed.Count} of {requestInfos.Count} request(s) failed.";
                }
                else
                {
                    run.Status = "Completed";
                }
                run.CompletedAt = DateTimeOffset.UtcNow;

                _logger.LogInformation("Lab VM run {RunId} finished: {Status}", runId, run.Status);
            }
            catch (Exception ex)
            {
                run.Status = "Failed";
                run.Error = ex.Message;
                run.CompletedAt = DateTimeOffset.UtcNow;
                _logger.LogError(ex, "Lab VM run {RunId} background task failed", runId);
            }

            // Persist to SQL for badge display on future page loads
            await PersistLabVmRunAsync(tenantGuid, run, submittedBy);
        });

        return Json(new
        {
            success = true,
            runId,
            requests = requestInfos.Select(r => new { r.RequestId, r.Index, r.Os, r.TargetServer, r.Status })
        });
    }

    /// <summary>
    /// Returns current run status including per-request progress.
    /// </summary>
    [HttpGet]
    public IActionResult Status(string runId)
    {
        if (GetAuthLevel() < (byte)AuthLevels.TenantReadOnly)
            return Json(new { success = false, error = "Permission denied." });

        if (string.IsNullOrWhiteSpace(runId))
            return Json(new { success = false, error = "Missing run ID." });

        var run = _tracker.Get(runId);
        if (run == null)
            return Json(new { success = false, error = "Run not found. It may have expired after an app restart." });

        return Json(new
        {
            success = true,
            runId = run.RunId,
            status = run.Status,
            startedAt = run.StartedAt,
            completedAt = run.CompletedAt,
            tenant = new { run.TenantId, run.TenantName, run.Lab },
            submittedBy = run.SubmittedBy,
            error = run.Error,
            requests = run.Requests.Select(r => new
            {
                r.RequestId,
                r.Index,
                r.Os,
                r.TargetServer,
                r.Status,
                r.Message,
                r.CreatedVmName
            })
        });
    }

    // --- Private helpers ---

    private async Task ExecuteServerBatchAsync(
        LabVmRunInfo run,
        List<LabVmRequestInfo> requests,
        int tenantId,
        string adminPassword,
        string tenantUserPassword,
        string hostIp)
    {
        // Run each VM request as its own SSH command so the tracker updates
        // per-request as each one completes (not all-at-once at the end).
        foreach (var req in requests)
        {
            req.Status = "Running";

            var sb = new StringBuilder();
            sb.AppendLine("$ErrorActionPreference = 'Stop'");
            sb.AppendLine($"$ap = ConvertTo-SecureString '{Escape(adminPassword)}' -AsPlainText -Force");
            sb.AppendLine($"$ac = [pscredential]::new('{Escape(AdminUserName)}', $ap)");
            sb.AppendLine($"$up = ConvertTo-SecureString '{Escape(tenantUserPassword)}' -AsPlainText -Force");
            sb.AppendLine($"$uc = [pscredential]::new('{Escape(TenantUserName)}', $up)");
            sb.AppendLine("try {");
            sb.AppendLine($"  $r = Start-LabVmRequest -TenantID {tenantId} -OS '{Escape(req.Os)}' -RunId '{Escape(req.RequestId)}' -AdminCred $ac -UserCred $uc -Confirm:$false 3>$null 6>$null");
            sb.AppendLine("} catch {");
            sb.AppendLine($"  $r = [pscustomobject]@{{ RunId='{Escape(req.RequestId)}'; Status='Failed'; Success=$false; Message=$_.Exception.Message; CreatedVmNames=@() }}");
            sb.AppendLine("}");
            sb.AppendLine($"Write-Output '{JsonMarker}'");
            sb.AppendLine("$r | ConvertTo-Json -Compress -Depth 5");

            try
            {
                var (success, output) = await _sshService.RunPowerShellCommandWithCredentialsAsync(
                    hostIp, 22, AdminUserName, adminPassword, sb.ToString(), keepAlive: TimeSpan.FromSeconds(15));

                if (!success)
                {
                    req.Status = "Failed";
                    req.Message = output;
                    _logger.LogError("Lab VM SSH failed for {RequestId} on {Host}: {Output}", req.RequestId, hostIp, output);
                    continue;
                }

                ParseSingleResult(req, output);
            }
            catch (Exception ex)
            {
                req.Status = "Failed";
                req.Message = ex.Message;
                _logger.LogError(ex, "Lab VM SSH exception for {RequestId} on {Host}", req.RequestId, hostIp);
            }
        }
    }

    private void ParseSingleResult(LabVmRequestInfo req, string output)
    {
        var markerIndex = output.LastIndexOf(JsonMarker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            req.Status = "Failed";
            req.Message = "No JSON marker found in SSH output.";
            return;
        }

        var jsonText = output[(markerIndex + JsonMarker.Length)..].Trim();
        try
        {
            var item = JsonSerializer.Deserialize<JsonElement>(jsonText);
            if (item.ValueKind != JsonValueKind.Object)
            {
                req.Status = "Failed";
                req.Message = "Unexpected JSON shape in SSH output.";
                return;
            }

            req.RawResult = item;
            req.Status = item.TryGetProperty("Status", out var s) ? s.GetString() ?? "Unknown" : "Unknown";
            req.Message = item.TryGetProperty("Message", out var m) ? m.GetString() : null;

            if (item.TryGetProperty("CreatedVmNames", out var names) && names.ValueKind == JsonValueKind.Array)
            {
                var vmNames = names.EnumerateArray()
                    .Where(n => n.ValueKind == JsonValueKind.String)
                    .Select(n => n.GetString())
                    .ToList();
                req.CreatedVmName = vmNames.Count > 0 ? string.Join(", ", vmNames) : null;
            }
        }
        catch (JsonException ex)
        {
            req.Status = "Failed";
            req.Message = $"Failed to parse SSH output: {ex.Message}";
        }
    }

    private static List<VmRequest> ParseVmRequests(string configContent)
    {
        return configContent
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("New-LabVM ", StringComparison.OrdinalIgnoreCase))
            .Select((command, index) =>
            {
                var osMatch = System.Text.RegularExpressions.Regex.Match(command,
                    @"(?:^|\s)-OS\s+(?<os>[A-Za-z0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return new VmRequest
                {
                    Index = index + 1,
                    Os = osMatch.Success ? osMatch.Groups["os"].Value : "Server2025"
                };
            })
            .ToList();
    }

    /// <summary>
    /// Derives the default Hyper-V server name from the tenant's lab and ID.
    /// TenantId 18 → first digit 1 → SEA-ER-01; TenantId 81 → SEA-ER-08.
    /// </summary>
    private static string GetDefaultServerName(string lab, int tenantId)
    {
        var serverNumber = tenantId / 10;
        return $"{lab}-ER-{serverNumber:00}";
    }

    private static string Escape(string value) => value.Replace("'", "''");

    private static SecretClient CreateSecretClient(string vaultName) =>
        new(new Uri($"https://{vaultName.ToLowerInvariant()}.vault.azure.net/"), new DefaultAzureCredential());

    private async Task PersistLabVmRunAsync(Guid tenantGuid, LabVmRunInfo run, string submittedBy)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LabConfigContext>();

            var allVmNames = run.Requests
                .Where(r => !string.IsNullOrEmpty(r.CreatedVmName))
                .Select(r => r.CreatedVmName)
                .ToList();

            var outputLines = run.Requests
                .Select(r => $"[{r.Index}] {r.Os} → {r.TargetServer}: {r.Status}" + (r.Message != null ? $" — {r.Message}" : ""))
                .ToList();

            db.LabVmRuns.Add(new Models.LabVmRun
            {
                LabVmRunId = Guid.NewGuid(),
                TenantGuid = tenantGuid,
                RunId = run.RunId,
                Success = run.Status == "Completed",
                Status = run.Status,
                RequestCount = run.Requests.Count,
                CreatedVmNames = allVmNames.Count > 0 ? string.Join(", ", allVmNames) : null,
                SubmittedBy = submittedBy,
                SubmittedDate = run.StartedAt.UtcDateTime,
                CompletedDate = run.CompletedAt?.UtcDateTime,
                Output = string.Join("\n", outputLines)
            });

            await db.SaveChangesAsync();
            _logger.LogInformation("Lab VM run {RunId} persisted to SQL", run.RunId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist Lab VM run {RunId} to SQL", run.RunId);
        }
    }

    private class VmRequest
    {
        public int Index { get; set; }
        public string Os { get; set; } = "Server2025";
        public string TargetServer { get; set; } = string.Empty;
    }
}

public sealed class LabVmSubmitRequest
{
    public Guid TenantGuid { get; set; }
    public List<LabVmOverride>? VmOverrides { get; set; }
}

public sealed class LabVmOverride
{
    public int Index { get; set; }
    public string? TargetServer { get; set; }
}
