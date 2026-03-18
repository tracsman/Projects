using System.Security.Claims;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using PathWeb.Data;
using PathWeb.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure EF Core with Entra ID authentication to Azure SQL
builder.Services.AddDbContext<LabConfigContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("LabConfig");
    options.UseSqlServer(connectionString);
});

// Register AuthLevel service
builder.Services.AddScoped<AuthLevelService>();

// Register ConfigGenerator service
builder.Services.AddScoped<ConfigGenerator>();

// Register Logic App service for ADO work item integration
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<LogicAppService>();
builder.Services.Configure<AutomationOptions>(builder.Configuration.GetSection("Automation"));
builder.Services.AddScoped<AutomationService>();

// Register memory cache for field help tooltips
builder.Services.AddMemoryCache();

// Register SSH service for device connectivity
var keyVaultUri = builder.Configuration["KeyVaultUri"];
if (!string.IsNullOrEmpty(keyVaultUri))
{
    builder.Services.AddSingleton(new SecretClient(new Uri(keyVaultUri), new DefaultAzureCredential()));
}
else
{
    // Local dev fallback — register a placeholder so DI doesn't fail at startup
    builder.Services.AddSingleton(new SecretClient(new Uri("https://localhost/"), new DefaultAzureCredential()));
}
builder.Services.AddScoped<SshService>();

// Check if running on Azure App Service with Easy Auth
var isEasyAuth = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
var easyAuthEnabled = isEasyAuth && builder.Configuration.GetValue<bool>("AzureAd:UseEasyAuth", true);

// Check if we have valid Entra ID configuration for code-based auth
var tenantId = builder.Configuration["AzureAd:TenantId"];
var hasValidEntraId = !string.IsNullOrEmpty(tenantId) && 
                      !tenantId.StartsWith("YOUR_") && 
                      !tenantId.StartsWith("PRODUCTION_");

if (easyAuthEnabled)
{
    // Easy Auth mode - App Service handles authentication at platform level
    // We just need to read the authenticated user from the headers
    builder.Services.AddAuthentication("EasyAuth")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, EasyAuthAuthenticationHandler>(
            "EasyAuth", options => { });

    // NO authorization requirement - Easy Auth already blocks unauthenticated requests
    builder.Services.AddControllersWithViews();
    builder.Services.AddRazorPages();
}
else if (hasValidEntraId)
{
    // Code-based Entra ID authentication (for local development or non-App Service hosting)
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddAuthorization(options =>
    {
        options.FallbackPolicy = options.DefaultPolicy;
    });

    builder.Services.AddControllersWithViews(options =>
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new AuthorizeFilter(policy));
    });

    builder.Services.AddRazorPages()
        .AddMicrosoftIdentityUI();
}
else
{
    // Development mode - fake identity from DevUser setting in appsettings.Development.json
    builder.Services.AddControllersWithViews();
    builder.Services.AddRazorPages();
}

var app = builder.Build();

// Register the database logger provider (must happen after Build so DI is available)
app.Services.GetRequiredService<ILoggerFactory>()
    .AddProvider(new DbLoggerProvider(
        app.Services.GetRequiredService<IServiceScopeFactory>(),
        app.Services.GetRequiredService<IHttpContextAccessor>()));

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Prevent browser caching of dynamic pages so stale content is never served
app.Use(async (context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/lib") &&
        !context.Request.Path.StartsWithSegments("/css") &&
        !context.Request.Path.StartsWithSegments("/js") &&
        !context.Request.Path.StartsWithSegments("/images"))
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";
        context.Response.Headers["Expires"] = "0";
    }
    await next();
});

app.UseRouting();

// Health check endpoint - returns build timestamp, placed before auth so it's always accessible
app.MapGet("/health", () =>
{
    var buildTime = GetBuildTimestamp();
    return Results.Ok(new { status = "healthy", build = buildTime });
}).AllowAnonymous();

// Warmup endpoint - forces JIT compilation of EF Core query plans, DI pipeline, and validates SQL connectivity
app.MapGet("/warmup", async (LabConfigContext db, IMemoryCache cache) =>
{
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Resolving LabConfigContext compiles the EF Core model; SELECT 1 validates SQL connectivity
        _ = await db.Database.ExecuteSqlRawAsync("SELECT 1");

        // Pre-compile the actual LINQ query shapes used by each page (Take(1) keeps data transfer minimal)
        // Tenants Index: Where + OrderBy + ThenBy
        _ = await db.Tenants.Where(t => t.DeletedDate == null).OrderBy(t => t.Lab).ThenBy(t => t.TenantId).Take(1).ToListAsync();
        // Tenants reverse sorts
        _ = await db.Tenants.Where(t => t.DeletedDate == null).OrderBy(t => t.NinjaOwner).Take(1).ToListAsync();
        // Devices Index: OrderBy + ThenBy
        _ = await db.Devices.OrderBy(d => d.Lab).ThenBy(d => d.Name).Take(1).ToListAsync();
        // Users Index
        _ = await db.Users.OrderBy(u => u.UserName).Take(1).ToListAsync();
        // Requests Index: OrderByDescending + Where with Contains
        _ = await db.TenantRequests.OrderByDescending(r => r.RequestedDate).Take(1).ToListAsync();
        _ = await db.TenantRequests.Where(r => r.RequestedBy == "" || (r.Contacts != null && r.Contacts.Contains(""))).Take(1).ToListAsync();
        // Regions dropdown
        _ = await db.Regions.OrderBy(r => r.Region1).Take(1).ToListAsync();
        // Config lookup
        _ = await db.Configs.Where(c => c.TenantGuid == Guid.Empty).ToListAsync();
        // Single entity lookups (FirstOrDefaultAsync / FindAsync patterns)
        _ = await db.Tenants.FirstOrDefaultAsync(t => t.TenantGuid == Guid.Empty);
        _ = await db.Devices.FirstOrDefaultAsync(d => d.DeviceId == Guid.Empty);
        _ = await db.Users.FirstOrDefaultAsync(u => u.UserName == "");
        // IP Addresses
        _ = await db.PublicIps.OrderBy(p => p.Lab).Take(1).ToListAsync();
        // Logs Index: OrderByDescending + Where with Level filter
        _ = await db.AppLogs.OrderByDescending(l => l.Id).Take(1).ToListAsync();

        // Pre-populate the FieldHelp tooltip cache
        var helpDict = await db.FieldHelps.AsNoTracking()
            .ToDictionaryAsync(f => f.FieldName, f => f.HelpText);
        cache.Set("FieldHelpDictionary", helpDict, TimeSpan.FromMinutes(30));

        sw.Stop();
        var buildTime = GetBuildTimestamp();
        return Results.Ok(new { status = "warm", build = buildTime, dbMs = sw.ElapsedMilliseconds });
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            status = "warmup-error",
            build = GetBuildTimestamp(),
            error = ex.Message
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

// Diagnostic endpoint - authenticated snapshot of environment, auth, DB, Azure integrations, and optional deep probes.
app.MapGet("/diag", async (
    HttpContext ctx,
    LabConfigContext db,
    IConfiguration config,
    IMemoryCache cache,
    AutomationService automationService,
    SecretClient secretClient,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    static bool IsTerminalAutomationStatus(string? status) =>
        status is "Completed" or "Failed" or "Stopped" or "Suspended";

    static string? GetHostOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return null;

        return uri.GetLeftPart(UriPartial.Authority);
    }

    static string? MaskValue(string? value, int prefixLength = 8)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        return value.Length <= prefixLength ? value : $"{value.Substring(0, prefixLength)}...";
    }

    async Task<bool> TableExistsAsync(string tableName)
    {
        var connection = db.Database.GetDbConnection();
        var closeWhenDone = false;

        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            closeWhenDone = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.tables WHERE name = @name AND schema_id = SCHEMA_ID('dbo')) THEN 1 ELSE 0 END";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@name";
            parameter.Value = tableName;
            command.Parameters.Add(parameter);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is 1 or true;
        }
        finally
        {
            if (closeWhenDone)
                await connection.CloseAsync();
        }
    }

    async Task<object> ProbeLogicAppHostAsync(string? triggerUrl)
    {
        var host = GetHostOnly(triggerUrl);
        if (string.IsNullOrWhiteSpace(host))
            return new { success = false, error = "Logic App trigger URL not configured." };

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            var client = httpClientFactory.CreateClient();
            using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, host), timeoutCts.Token);
            return new
            {
                success = true,
                host,
                statusCode = (int)response.StatusCode
            };
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                host,
                error = ex.Message
            };
        }
    }

    var buildTime = GetBuildTimestamp();
    var dotnetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    var deepRequested = string.Equals(ctx.Request.Query["deep"], "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ctx.Request.Query["deep"], "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ctx.Request.Query["deep"], "yes", StringComparison.OrdinalIgnoreCase);

    var isAppService = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
    var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "(local)";
    var slotName = Environment.GetEnvironmentVariable("WEBSITE_SLOT_NAME") ?? "production";
    var instanceIdRaw = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID");
    var instanceId = instanceIdRaw != null ? instanceIdRaw.Substring(0, Math.Min(8, instanceIdRaw.Length)) : "(local)";
    var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    var managedIdentityAvailable =
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT")) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MSI_ENDPOINT"));

    var principalName = ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].FirstOrDefault();
    var userName = ctx.User?.Identity?.Name ?? "(anonymous)";
    var userEmail = ctx.User?.FindFirst(ClaimTypes.Email)?.Value ?? "(none)";
    var authType = easyAuthEnabled ? "Easy Auth" : ctx.User?.Identity?.IsAuthenticated == true ? "OIDC / Dev Identity" : "None";
    var hasClientPrincipal = !string.IsNullOrEmpty(ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault());
    var hasAccessToken = !string.IsNullOrEmpty(ctx.Request.Headers["X-MS-TOKEN-AAD-ACCESS-TOKEN"].FirstOrDefault());
    var hasIdToken = !string.IsNullOrEmpty(ctx.Request.Headers["X-MS-TOKEN-AAD-ID-TOKEN"].FirstOrDefault());
    var authLevel = ctx.Items["AuthLevel"] as byte? ?? 0;

    var keyVaultUri = config["KeyVaultUri"];
    var keyVaultHost = GetHostOnly(keyVaultUri);
    var deviceCredentialUser = config["DeviceCredentials:Username"];
    var deviceCredentialUserConfigured = !string.IsNullOrWhiteSpace(deviceCredentialUser);
    var logicAppTriggerUrl = config["LogicApp:TriggerUrl"];
    var logicAppConfigured = !string.IsNullOrWhiteSpace(logicAppTriggerUrl);
    var logicAppHost = GetHostOnly(logicAppTriggerUrl);
    var adoOrg = config["AzureDevOps:OrgUrl"];
    var adoProject = config["AzureDevOps:Project"];
    var adoWorkItemType = config["AzureDevOps:WorkItemType"];
    var automationSubscriptionId = config["Automation:SubscriptionId"];
    var automationResourceGroup = config["Automation:ResourceGroupName"];
    var automationAccountName = config["Automation:AccountName"];
    var automationLocation = config["Automation:Location"];

    var configurationWarnings = new List<string>();
    if (!automationService.IsConfigured)
        configurationWarnings.Add(automationService.GetConfigurationError() ?? "Automation configuration is incomplete.");
    if (!logicAppConfigured)
        configurationWarnings.Add("LogicApp:TriggerUrl is not configured.");
    if (string.IsNullOrWhiteSpace(keyVaultUri))
        configurationWarnings.Add("KeyVaultUri is not configured.");
    if (!deviceCredentialUserConfigured)
        configurationWarnings.Add("DeviceCredentials:Username is not configured.");
    if (isAppService && !managedIdentityAvailable)
        configurationWarnings.Add("Managed identity environment variables are not present on this App Service instance.");

    var dbConnected = false;
    string dbStatus;
    long dbMs;
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _ = await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
        sw.Stop();
        dbMs = sw.ElapsedMilliseconds;
        dbStatus = "connected";
        dbConnected = true;
    }
    catch (Exception ex)
    {
        dbMs = -1;
        dbStatus = $"error: {ex.Message}";
        configurationWarnings.Add("Database connectivity failed. See database.status for details.");
    }

    int? tenantCount = null;
    int? releasedTenantCount = null;
    int? pendingRequests = null;
    int? userCount = null;
    int? configRowCount = null;
    int? automationRunCount = null;
    int? appLogCount = null;
    object? tableChecks = null;
    object? loggingInfo = null;
    object? deviceInfo = null;
    object? automationInfo = null;
    string? dataCollectionError = null;

    if (dbConnected)
    {
        try
        {
            tenantCount = await db.Tenants.CountAsync(t => t.DeletedDate == null, cancellationToken);
            releasedTenantCount = await db.Tenants.CountAsync(t => t.DeletedDate != null, cancellationToken);
            pendingRequests = await db.TenantRequests.CountAsync(r => r.Status == "Pending", cancellationToken);
            userCount = await db.Users.CountAsync(cancellationToken);
            configRowCount = await db.Configs.CountAsync(cancellationToken);
            automationRunCount = await db.AutomationRuns.CountAsync(cancellationToken);
            appLogCount = await db.AppLogs.CountAsync(cancellationToken);

            tableChecks = new
            {
                tenant = await TableExistsAsync("Tenant"),
                users = await TableExistsAsync("Users"),
                config = await TableExistsAsync("Config"),
                appLog = await TableExistsAsync("AppLog"),
                automationRun = await TableExistsAsync("AutomationRun")
            };

            var recentSince = DateTime.UtcNow.AddHours(-24);
            var warningCount = await db.AppLogs.CountAsync(l => l.Timestamp >= recentSince && l.Level == "Warning", cancellationToken);
            var errorCount = await db.AppLogs.CountAsync(l => l.Timestamp >= recentSince && (l.Level == "Error" || l.Level == "Critical"), cancellationToken);
            var latestErrorsRaw = await db.AppLogs
                .AsNoTracking()
                .Where(l => l.Level == "Error" || l.Level == "Critical")
                .OrderByDescending(l => l.Timestamp)
                .Take(5)
                .Select(l => new
                {
                    l.Timestamp,
                    l.Category,
                    l.Message
                })
                .ToListAsync(cancellationToken);
            var latestErrors = latestErrorsRaw.Select(l => new
            {
                l.Timestamp,
                l.Category,
                message = l.Message.Length > 200 ? l.Message.Substring(0, 200) : l.Message
            });
            loggingInfo = new
            {
                totalRows = appLogCount,
                warningsLast24h = warningCount,
                errorsLast24h = errorCount,
                latestErrors
            };

            var devices = await db.Devices
                .AsNoTracking()
                .Select(d => new { d.Name, d.Lab, d.InService })
                .ToListAsync(cancellationToken);
            var sshLatestErrorRaw = await db.AppLogs
                .AsNoTracking()
                .Where(l => l.Category.Contains("SshService") && (l.Level == "Error" || l.Level == "Warning"))
                .OrderByDescending(l => l.Timestamp)
                .Select(l => new
                {
                    l.Timestamp,
                    l.Level,
                    l.Message
                })
                .FirstOrDefaultAsync(cancellationToken);
            var sshLatestError = sshLatestErrorRaw == null ? null : new
            {
                sshLatestErrorRaw.Timestamp,
                sshLatestErrorRaw.Level,
                message = sshLatestErrorRaw.Message.Length > 200 ? sshLatestErrorRaw.Message.Substring(0, 200) : sshLatestErrorRaw.Message
            };
            deviceInfo = new
            {
                total = devices.Count,
                inService = devices.Count(d => d.InService),
                byPlatform = new
                {
                    juniper = devices.Count(d => PlatformDetector.DetectPlatform(d.Name) == "Juniper"),
                    nxos = devices.Count(d => PlatformDetector.DetectPlatform(d.Name) == "NX-OS"),
                    iosxe = devices.Count(d => PlatformDetector.DetectPlatform(d.Name) == "IOS-XE"),
                    unknown = devices.Count(d => PlatformDetector.DetectPlatform(d.Name) == "Unknown")
                },
                byLab = devices
                    .GroupBy(d => d.Lab)
                    .OrderBy(g => g.Key)
                    .ToDictionary(g => g.Key.Trim(), g => g.Count()),
                latestSshIssue = sshLatestError
            };

            var automationRuns = await db.AutomationRuns
                .AsNoTracking()
                .Where(r => r.ConfigType == "CreateERPowerShell" || r.ConfigType == "CreateAzurePowerShell")
                .OrderByDescending(r => r.SubmittedDate)
                .ToListAsync(cancellationToken);
            var latestAutomationByConfig = automationRuns
                .GroupBy(r => r.ConfigType)
                .ToDictionary(
                    g => g.Key,
                    g => g.First());
            var latestAutomationIssueRaw = await db.AppLogs
                .AsNoTracking()
                .Where(l => l.Category.Contains("AutomationService") && (l.Level == "Error" || l.Level == "Warning"))
                .OrderByDescending(l => l.Timestamp)
                .Select(l => new
                {
                    l.Timestamp,
                    l.Level,
                    l.Message
                })
                .FirstOrDefaultAsync(cancellationToken);
            var latestAutomationIssue = latestAutomationIssueRaw == null ? null : new
            {
                latestAutomationIssueRaw.Timestamp,
                latestAutomationIssueRaw.Level,
                message = latestAutomationIssueRaw.Message.Length > 200 ? latestAutomationIssueRaw.Message.Substring(0, 200) : latestAutomationIssueRaw.Message
            };
            automationInfo = new
            {
                configured = automationService.IsConfigured,
                subscriptionId = MaskValue(automationSubscriptionId),
                resourceGroup = automationResourceGroup,
                accountName = automationAccountName,
                location = automationLocation,
                totalRuns = automationRuns.Count,
                running = automationRuns.Count(r => !IsTerminalAutomationStatus(r.Status)),
                completed = automationRuns.Count(r => r.Status == "Completed"),
                failed = automationRuns.Count(r => r.Status == "Failed"),
                oldestActiveRunAgeMinutes = automationRuns
                    .Where(r => !IsTerminalAutomationStatus(r.Status))
                    .Select(r => (double?)Math.Round((DateTime.UtcNow - r.SubmittedDate).TotalMinutes, 1))
                    .OrderByDescending(v => v)
                    .FirstOrDefault(),
                latestByConfig = latestAutomationByConfig.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new
                    {
                        kvp.Value.JobId,
                        kvp.Value.RunbookName,
                        kvp.Value.Status,
                        kvp.Value.SubmittedDate,
                        kvp.Value.CompletedDate
                    }),
                latestIssue = latestAutomationIssue
            };
        }
        catch (Exception ex)
        {
            dataCollectionError = ex.Message;
        }
    }

    cache.TryGetValue("FieldHelpDictionary", out Dictionary<string, string>? fieldHelpDictionary);

    var logicAppLatestIssueRaw = dbConnected
        ? await db.AppLogs
            .AsNoTracking()
            .Where(l => l.Category.Contains("LogicAppService") && (l.Level == "Error" || l.Level == "Warning"))
            .OrderByDescending(l => l.Timestamp)
            .Select(l => new
            {
                l.Timestamp,
                l.Level,
                l.Message
            })
            .FirstOrDefaultAsync(cancellationToken)
        : null;
    var logicAppLatestIssue = logicAppLatestIssueRaw == null ? null : new
    {
        logicAppLatestIssueRaw.Timestamp,
        logicAppLatestIssueRaw.Level,
        message = logicAppLatestIssueRaw.Message.Length > 200 ? logicAppLatestIssueRaw.Message.Substring(0, 200) : logicAppLatestIssueRaw.Message
    };

    object? deepProbes = null;
    if (deepRequested)
    {
        object keyVaultProbe;
        if (string.IsNullOrWhiteSpace(keyVaultUri) || !deviceCredentialUserConfigured)
        {
            keyVaultProbe = new { success = false, error = "Key Vault or device credential username is not configured." };
        }
        else
        {
            try
            {
                var secret = await secretClient.GetSecretAsync(deviceCredentialUser!, cancellationToken: cancellationToken);
                keyVaultProbe = new
                {
                    success = true,
                    secretName = secret.Value.Name,
                    enabled = secret.Value.Properties.Enabled,
                    updatedOn = secret.Value.Properties.UpdatedOn
                };
            }
            catch (Exception ex)
            {
                keyVaultProbe = new { success = false, error = ex.Message };
            }
        }

        var automationProbe = await automationService.ProbeAccountAsync(cancellationToken);
        deepProbes = new
        {
            keyVault = keyVaultProbe,
            automation = new
            {
                automationProbe.Success,
                automationProbe.AccountName,
                automationProbe.Location,
                automationProbe.Error
            },
            logicApp = await ProbeLogicAppHostAsync(logicAppTriggerUrl)
        };
    }

    return Results.Ok(new
    {
        build = buildTime,
        runtime = dotnetVersion,
        deepMode = deepRequested,
        environment = new
        {
            site = siteName,
            slot = slotName,
            instance = instanceId,
            isAppService,
            environmentName,
            easyAuthEnabled,
            managedIdentityAvailable
        },
        auth = new
        {
            type = authType,
            user = userName,
            email = userEmail,
            easyAuthPrincipal = principalName ?? "(none)",
            authLevel,
            hasClientPrincipal,
            hasAccessToken,
            hasIdToken
        },
        configuration = new
        {
            warnings = configurationWarnings,
            azureDevOps = new
            {
                org = adoOrg,
                project = adoProject,
                workItemType = adoWorkItemType
            },
            automation = new
            {
                configured = automationService.IsConfigured,
                subscriptionId = MaskValue(automationSubscriptionId),
                resourceGroup = automationResourceGroup,
                accountName = automationAccountName,
                location = automationLocation
            },
            logicApp = new
            {
                configured = logicAppConfigured,
                host = logicAppHost
            },
            keyVault = new
            {
                configured = !string.IsNullOrWhiteSpace(keyVaultUri),
                host = keyVaultHost,
                deviceCredentialUserConfigured
            }
        },
        database = new
        {
            status = dbStatus,
            latencyMs = dbMs,
            activeTenants = tenantCount,
            releasedTenants = releasedTenantCount,
            pendingRequests,
            users = userCount,
            configRows = configRowCount,
            automationRuns = automationRunCount,
            appLogs = appLogCount,
            tables = tableChecks,
            dataCollectionError
        },
        cache = new
        {
            fieldHelpLoaded = fieldHelpDictionary != null,
            fieldHelpEntryCount = fieldHelpDictionary?.Count ?? 0
        },
        logging = loggingInfo,
        devices = deviceInfo,
        automation = automationInfo,
        integrations = new
        {
            logicAppConfigured,
            logicAppHost,
            latestLogicAppIssue = logicAppLatestIssue
        },
        keyVault = new
        {
            configured = !string.IsNullOrWhiteSpace(keyVaultUri),
            host = keyVaultHost,
            managedIdentityAvailable,
            deviceCredentialUserConfigured
        },
        deepProbes
    });
});

if (easyAuthEnabled || hasValidEntraId)
{
    app.UseAuthentication();
}
app.UseAuthorization();

// Development fake identity - creates an authenticated user from the DevUser config setting
if (app.Environment.IsDevelopment() && !easyAuthEnabled && !hasValidEntraId)
{
    var devUser = app.Configuration["DevUser"];
    if (!string.IsNullOrEmpty(devUser))
    {
        app.Use(async (context, next) =>
        {
            var claims = new[] {
                new Claim(ClaimTypes.Name, devUser),
                new Claim(ClaimTypes.Email, devUser)
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Development"));
            await next();
        });
    }
}

// AuthLevel middleware - looks up the user's AuthLevel from the database on each request
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/health", StringComparison.OrdinalIgnoreCase) ||
        context.Request.Path.Equals("/warmup", StringComparison.OrdinalIgnoreCase))
    {
        context.Items["AuthLevel"] = (byte)0;
        await next();
        return;
    }

    if (context.User.Identity?.IsAuthenticated == true)
    {
        var authService = context.RequestServices.GetRequiredService<AuthLevelService>();
        // Use email claim for lookup (matches UserName column in Users table)
        var email = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? context.User.Identity.Name;
        var authLevel = await authService.GetAuthLevelAsync(email);
        context.Items["AuthLevel"] = authLevel;
    }
    else
    {
        context.Items["AuthLevel"] = (byte)0;
    }
    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();

// Reads build timestamp from build.json (written by deploy script) for exact match during deploy verification.
// Falls back to DLL LastWriteTime for local development where build.json doesn't exist.
static string GetBuildTimestamp()
{
    try
    {
        var buildJsonPath = Path.Combine(AppContext.BaseDirectory, "build.json");
        if (File.Exists(buildJsonPath))
        {
            var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(buildJsonPath));
            if (json.RootElement.TryGetProperty("build", out var buildProp))
                return buildProp.GetString() ?? FallbackBuildTime();
        }
    }
    catch { }
    return FallbackBuildTime();
}

static string FallbackBuildTime() =>
    File.GetLastWriteTime(typeof(Program).Assembly.Location).ToString("yyyy-MM-dd HH:mm:ss");
