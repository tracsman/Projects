using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddScoped<LogicAppService>();

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
    var buildTime = System.IO.File.GetLastWriteTime(typeof(Program).Assembly.Location)
        .ToString("yyyy-MM-dd HH:mm:ss");
    return Results.Ok(new { status = "healthy", build = buildTime });
}).AllowAnonymous();

// Warmup endpoint - forces JIT compilation of EF Core model, DI pipeline, and validates SQL connectivity
app.MapGet("/warmup", async (LabConfigContext db) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    // Resolving LabConfigContext compiles the EF Core model; SELECT 1 validates SQL connectivity
    _ = await db.Database.ExecuteSqlRawAsync("SELECT 1");
    sw.Stop();
    var buildTime = System.IO.File.GetLastWriteTime(typeof(Program).Assembly.Location)
        .ToString("yyyy-MM-dd HH:mm:ss");
    return Results.Ok(new { status = "warm", build = buildTime, dbMs = sw.ElapsedMilliseconds });
}).AllowAnonymous();

// Diagnostic endpoint - authenticated, shows environment, auth, DB, and runtime info for troubleshooting
app.MapGet("/diag", async (HttpContext ctx, LabConfigContext db, IConfiguration config) =>
{
    // Build info
    var assembly = typeof(Program).Assembly;
    var buildTime = System.IO.File.GetLastWriteTime(assembly.Location).ToString("yyyy-MM-dd HH:mm:ss");
    var dotnetVersion = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    // Environment
    var isAppService = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));
    var siteName = Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") ?? "(local)";
    var instanceId = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")?[..Math.Min(8, Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")?.Length ?? 0)] ?? "(local)";

    // Auth info
    var principalName = ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL-NAME"].FirstOrDefault();
    var userName = ctx.User?.Identity?.Name ?? "(anonymous)";
    var userEmail = ctx.User?.FindFirst(ClaimTypes.Email)?.Value ?? "(none)";
    var authType = isAppService ? "Easy Auth" : ctx.User?.Identity?.IsAuthenticated == true ? "OIDC / Dev Identity" : "None";

    // Easy Auth headers present
    var hasClientPrincipal = !string.IsNullOrEmpty(ctx.Request.Headers["X-MS-CLIENT-PRINCIPAL"].FirstOrDefault());
    var hasAccessToken = !string.IsNullOrEmpty(ctx.Request.Headers["X-MS-TOKEN-AAD-ACCESS-TOKEN"].FirstOrDefault());
    var hasIdToken = !string.IsNullOrEmpty(ctx.Request.Headers["X-MS-TOKEN-AAD-ID-TOKEN"].FirstOrDefault());

    // DB connectivity
    string dbStatus;
    long dbMs;
    try
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _ = await db.Database.ExecuteSqlRawAsync("SELECT 1");
        sw.Stop();
        dbMs = sw.ElapsedMilliseconds;
        dbStatus = "connected";
    }
    catch (Exception ex)
    {
        dbMs = -1;
        dbStatus = $"error: {ex.Message}";
    }

    // DB stats
    var tenantCount = await db.Tenants.CountAsync(t => t.DeletedDate == null);
    var pendingRequests = await db.TenantRequests.CountAsync(r => r.Status == "Pending");
    var userCount = await db.Users.CountAsync();

    // Auth level for current user
    var authLevel = ctx.Items["AuthLevel"] as byte? ?? 0;

    return Results.Ok(new
    {
        build = buildTime,
        runtime = dotnetVersion,
        environment = new
        {
            site = siteName,
            instance = instanceId,
            isAppService
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
        database = new
        {
            status = dbStatus,
            latencyMs = dbMs,
            activeTenants = tenantCount,
            pendingRequests,
            users = userCount
        },
        config = new
        {
            adoOrg = config["AzureDevOps:OrgUrl"],
            adoProject = config["AzureDevOps:Project"],
            adoWorkItemType = config["AzureDevOps:WorkItemType"]
        }
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
