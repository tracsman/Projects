using System.Text;
using System.Text.Json;
using PathWeb.Models;

namespace PathWeb.Services;

public class LogicAppService
{
    private readonly IConfiguration _config;
    private readonly ILogger<LogicAppService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public LogicAppService(IConfiguration config, ILogger<LogicAppService> logger, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Creates an ADO work item via the Logic App. Returns (WorkItemId, Error).
    /// </summary>
    public async Task<(int WorkItemId, string? Error)> CreateWorkItemAsync(Tenant tenant, TenantRequest request)
    {
        var triggerUrl = _config["LogicApp:TriggerUrl"];
        if (string.IsNullOrEmpty(triggerUrl))
            return (0, "LogicApp:TriggerUrl not configured. ADO integration is disabled.");

        try
        {
            var payload = new
            {
                action = "create",
                workItemId = 0,
                title = $"Lab Request: {tenant.TenantName}",
                description = BuildDescription(tenant, request),
                comment = BuildCreateComment(tenant, request),
                assignedTo = tenant.NinjaOwner,
                areaPath = @"One\Networking\Core Experiences\Pathfinders\Lab",
                tags = "Lab Request; PathWeb"
            };

            var (body, error) = await PostToLogicApp(triggerUrl, payload);
            if (error != null)
                return (0, error);

            using var doc = JsonDocument.Parse(body!);
            var workItemId = doc.RootElement.GetProperty("workItemId").GetInt32();

            _logger.LogInformation("ADO work item {WorkItemId} created for {TenantName} via Logic App", workItemId, tenant.TenantName);
            return (workItemId, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create ADO work item for {TenantName}", tenant.TenantName);
            return (0, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates an existing ADO work item via the Logic App. Returns null on success, or error message.
    /// </summary>
    public async Task<string?> UpdateWorkItemAsync(Tenant tenant)
    {
        if (tenant.WorkItemId is null or 0)
            return "No WorkItemId to update.";

        var triggerUrl = _config["LogicApp:TriggerUrl"];
        if (string.IsNullOrEmpty(triggerUrl))
            return "LogicApp:TriggerUrl not configured. ADO integration is disabled.";

        try
        {
            var payload = new
            {
                action = "update",
                workItemId = tenant.WorkItemId,
                title = $"Lab Request: {tenant.TenantName}",
                description = BuildDescriptionFromTenant(tenant),
                comment = BuildUpdateComment(tenant),
                assignedTo = tenant.NinjaOwner,
                areaPath = @"One\Networking\Core Experiences\Pathfinders\Lab",
                tags = "Lab Request; PathWeb"
            };

            var (_, error) = await PostToLogicApp(triggerUrl, payload);
            if (error != null)
                return error;

            _logger.LogInformation("ADO work item {WorkItemId} updated for {TenantName} via Logic App", tenant.WorkItemId, tenant.TenantName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update ADO work item {WorkItemId} for {TenantName}", tenant.WorkItemId, tenant.TenantName);
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private async Task<(string? Body, string? Error)> PostToLogicApp(string url, object payload)
    {
        var http = _httpClientFactory.CreateClient();
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await http.PostAsync(url, content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Logic App call failed: {StatusCode} {Body}", response.StatusCode, body);
            return (null, $"Logic App returned {response.StatusCode}: {(body.Length > 200 ? body[..200] : body)}");
        }

        return (body, null);
    }

    private static string BuildDescription(Tenant tenant, TenantRequest request)
    {
        var sb = new StringBuilder();

        sb.Append("""
            <style>
            table { max-width:800px; border:1px solid #1C6EA4; border-collapse:collapse; background-color:linen; text-align:left; }
            td { border:1px solid #AAAAAA; padding:0in 5.4pt 0in 10px; }
            </style>
            """);

        sb.Append($"<h1>Lab Request Approved</h1>");
        sb.Append($"<p>Requested by: {request.RequestedBy}</p>");
        sb.Append($"<p>Requested date: {request.RequestedDate:u}</p>");
        sb.Append($"<p>Approved by: {tenant.AssignedBy}</p>");
        sb.Append($"<p>Approved date: {tenant.AssignedDate:u}</p>");
        sb.Append("<br/>");

        AppendTenantTable(sb, tenant);

        return sb.ToString();
    }

    private static string BuildDescriptionFromTenant(Tenant tenant)
    {
        var sb = new StringBuilder();

        sb.Append("""
            <style>
            table { max-width:800px; border:1px solid #1C6EA4; border-collapse:collapse; background-color:linen; text-align:left; }
            td { border:1px solid #AAAAAA; padding:0in 5.4pt 0in 10px; }
            </style>
            """);

        sb.Append($"<h1>Lab Request: {tenant.TenantName}</h1>");
        sb.Append($"<p>Last updated: {tenant.LastUpdateDate:u} by {tenant.LastUpdateBy}</p>");
        sb.Append("<br/>");

        AppendTenantTable(sb, tenant);

        return sb.ToString();
    }

    private static string BuildCreateComment(Tenant tenant, TenantRequest request)
    {
        var sb = new StringBuilder();
        sb.Append("<b>📋 Lab Request Created via PathWeb</b><br/><br/>");
        sb.Append($"<b>Requested by:</b> {request.RequestedBy}<br/>");
        sb.Append($"<b>Requested date:</b> {request.RequestedDate:yyyy-MM-dd HH:mm}<br/>");
        sb.Append($"<b>Approved by:</b> {tenant.AssignedBy}<br/>");
        sb.Append($"<b>Approved date:</b> {tenant.AssignedDate:yyyy-MM-dd HH:mm}<br/>");
        return sb.ToString();
    }

    private static string BuildUpdateComment(Tenant tenant)
    {
        var sb = new StringBuilder();
        sb.Append("<b>✏️ Tenant Updated via PathWeb</b><br/><br/>");
        sb.Append($"<b>Updated by:</b> {tenant.LastUpdateBy}<br/>");
        sb.Append($"<b>Updated date:</b> {tenant.LastUpdateDate:yyyy-MM-dd HH:mm}<br/>");
        return sb.ToString();
    }

    private static void AppendTenantTable(StringBuilder sb, Tenant tenant)
    {
        sb.Append("<table>");

        // Lab Tenant Information
        sb.Append("<tr><td colspan=2><b>Lab Tenant Information</b></td></tr>");
        sb.Append($"""<tr><td style="width:300px;">Tenant:</td><td style="width:500px;">{tenant.TenantName}</td></tr>""");
        sb.Append($"""<tr><td>Lab:</td><td>{tenant.Lab}</td></tr>""");
        sb.Append($"""<tr><td>Contacts:</td><td>{tenant.Contacts}</td></tr>""");
        sb.Append($"""<tr><td>Usage:</td><td>{tenant.Usage}</td></tr>""");
        sb.Append($"""<tr><td>Azure Region:</td><td>{tenant.AzureRegion}</td></tr>""");
        sb.Append("<tr><td colspan=2>&nbsp;</td></tr>");

        // ExpressRoute Config
        sb.Append("<tr><td colspan=2><b>ExpressRoute Config</b></td></tr>");
        sb.Append($"""<tr><td>SKU:</td><td>{tenant.Ersku}</td></tr>""");
        sb.Append($"""<tr><td>Speed:</td><td>{tenant.Erspeed} Mbps</td></tr>""");
        sb.Append($"""<tr><td>Uplink:</td><td>{tenant.EruplinkPort}</td></tr>""");
        sb.Append($"""<tr><td>Private Peering:</td><td>{(tenant.PvtPeering == true ? "Enabled" : "Not Enabled")}</td></tr>""");
        sb.Append($"""<tr><td>ER Gateway:</td><td>{tenant.ErgatewaySize}</td></tr>""");
        sb.Append($"""<tr><td>ER Fast Path:</td><td>{(tenant.ErfastPath == true ? "Enabled" : "Not Enabled")}</td></tr>""");
        sb.Append($"""<tr><td>Microsoft Peering:</td><td>{(tenant.Msftpeering == true ? "Enabled" : "Not Enabled")}</td></tr>""");
        sb.Append("<tr><td colspan=2>&nbsp;</td></tr>");

        // VPN Config
        sb.Append("<tr><td colspan=2><b>VPN Config</b></td></tr>");
        sb.Append($"""<tr><td>VPN Gateway SKU:</td><td>{tenant.Vpngateway}</td></tr>""");
        sb.Append($"""<tr><td>Enable BGP:</td><td>{(tenant.Vpnbgp == true ? "Yes" : "No")}</td></tr>""");
        sb.Append($"""<tr><td>Configuration:</td><td>{tenant.Vpnconfig}</td></tr>""");
        sb.Append("<tr><td colspan=2>&nbsp;</td></tr>");

        // Azure VM Details
        sb.Append("<tr><td colspan=2><b>Azure VM Details</b></td></tr>");
        sb.Append($"""<tr><td>Azure VM1:</td><td>{tenant.AzVm1}</td></tr>""");
        sb.Append($"""<tr><td>Azure VM2:</td><td>{tenant.AzVm2}</td></tr>""");
        sb.Append($"""<tr><td>Azure VM3:</td><td>{tenant.AzVm3}</td></tr>""");
        sb.Append($"""<tr><td>Azure VM4:</td><td>{tenant.AzVm4}</td></tr>""");
        sb.Append("<tr><td colspan=2>&nbsp;</td></tr>");

        // On-premise VM Details
        sb.Append("<tr><td colspan=2><b>On-premise VM Details</b></td></tr>");
        sb.Append($"""<tr><td>Address Family:</td><td>{tenant.AddressFamily}</td></tr>""");
        sb.Append($"""<tr><td>On-prem VM1:</td><td>{tenant.LabVm1}</td></tr>""");
        sb.Append($"""<tr><td>On-prem VM2:</td><td>{tenant.LabVm2}</td></tr>""");
        sb.Append($"""<tr><td>On-prem VM3:</td><td>{tenant.LabVm3}</td></tr>""");
        sb.Append($"""<tr><td>On-prem VM4:</td><td>{tenant.LabVm4}</td></tr>""");
        sb.Append("<tr><td colspan=2>&nbsp;</td></tr>");

        // Administrative Information
        sb.Append("<tr><td colspan=2><b>Administrative Information</b></td></tr>");
        sb.Append($"""<tr><td>Tenant GUID:</td><td>{tenant.TenantGuid}</td></tr>""");
        sb.Append($"""<tr><td>Tenant ID:</td><td>{tenant.TenantId}</td></tr>""");
        sb.Append($"""<tr><td>Ninja Owner:</td><td>{tenant.NinjaOwner}</td></tr>""");
        sb.Append($"""<tr><td>Return Date:</td><td>{tenant.ReturnDate:yyyy-MM-dd}</td></tr>""");
        sb.Append($"""<tr><td>Assigned Date:</td><td>{tenant.AssignedDate:u}</td></tr>""");
        sb.Append($"""<tr><td>Assigned By:</td><td>{tenant.AssignedBy}</td></tr>""");

        sb.Append("</table>");
    }
}
