using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;

namespace PathWeb.Services;

public sealed class LogicAppConnectionDiagnostics
{
    private static readonly Regex WorkflowIdPattern = new(@"^/subscriptions/[0-9a-fA-F-]{36}/resourceGroups/[^/]+/providers/Microsoft\.Logic/workflows/[^/]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ConnectionIdPattern = new(@"^/subscriptions/[0-9a-fA-F-]{36}/resourceGroups/[^/]+/providers/Microsoft\.Web/connections/[^/]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClients;
    private readonly ILogger<LogicAppConnectionDiagnostics> _logger;
    private readonly TokenCredential _credential;

    public LogicAppConnectionDiagnostics(IConfiguration configuration, IHttpClientFactory httpClients, ILogger<LogicAppConnectionDiagnostics> logger)
        : this(configuration, httpClients, logger, new DefaultAzureCredential()) { }

    public LogicAppConnectionDiagnostics(IConfiguration configuration, IHttpClientFactory httpClients, ILogger<LogicAppConnectionDiagnostics> logger, TokenCredential credential)
    {
        _configuration = configuration;
        _httpClients = httpClients;
        _logger = logger;
        _credential = credential;
    }

    public async Task<string> CheckAsync(string workflowSetting)
    {
        var workflowId = _configuration[workflowSetting];
        if (string.IsNullOrWhiteSpace(workflowId))
            return "Connection diagnostics are not configured. An administrator can inspect the Logic App run history and API connections.";
        if (!WorkflowIdPattern.IsMatch(workflowId))
        {
            _logger.LogWarning("Invalid Logic App workflow resource ID in {Setting}", workflowSetting);
            return "Connection diagnostics are misconfigured. Ask an administrator to check the workflow resource ID.";
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var token = await _credential.GetTokenAsync(new TokenRequestContext(["https://management.azure.com/.default"]), timeout.Token);
            var client = _httpClients.CreateClient();
            async Task<JsonDocument> GetResourceAsync(string id, string version)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://management.azure.com{id}?api-version={version}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
                using var response = await client.SendAsync(request, timeout.Token);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                return await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token);
            }

            using var workflow = await GetResourceAsync(workflowId, "2016-06-01");
            var connections = workflow.RootElement.GetProperty("properties").GetProperty("parameters").GetProperty("$connections").GetProperty("value");
            var issues = new List<string>();
            var checkedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var binding in connections.EnumerateObject())
            {
                if (!binding.Value.TryGetProperty("connectionId", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                    throw new InvalidOperationException("Workflow connection binding has no resource ID.");
                var id = idElement.GetString()!;
                if (!ConnectionIdPattern.IsMatch(id)) throw new InvalidOperationException("Workflow connection resource ID is invalid.");
                if (!checkedIds.Add(id)) continue;
                using var connection = await GetResourceAsync(id, "2016-06-01");
                var name = id.Split('/')[^1];
                var statuses = connection.RootElement.GetProperty("properties").GetProperty("statuses");
                if (statuses.ValueKind != JsonValueKind.Array || statuses.GetArrayLength() == 0)
                    throw new InvalidOperationException("Connection has no reported status.");
                foreach (var entry in statuses.EnumerateArray())
                {
                    var status = entry.GetProperty("status").GetString();
                    if (string.Equals(status, "Connected", StringComparison.OrdinalIgnoreCase)) continue;
                    var code = entry.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var codeValue) && codeValue.ValueKind == JsonValueKind.String ? codeValue.GetString() : null;
                    var isAuth = string.Equals(code, "Unauthorized", StringComparison.OrdinalIgnoreCase) || string.Equals(code, "Unauthenticated", StringComparison.OrdinalIgnoreCase);
                    issues.Add(isAuth
                        ? $"API connection '{name}' reports an authentication problem ({code}). An Azure administrator should check and reauthorize the connection using its intended identity; additional identity or credential repair may be needed."
                        : $"API connection '{name}' reports status '{status ?? "Unknown"}'. An Azure administrator should inspect its status and the Logic App run history.");
                }
            }
            if (checkedIds.Count == 0) return "The Logic App has no configured API connections to inspect. Check its run history.";
            return issues.Count == 0
                ? "The Logic App's API connections currently report Connected. Check the workflow run history for the actual failure."
                : string.Join(" ", issues);
        }
        catch (Exception ex) when (ex is Azure.RequestFailedException or Azure.Identity.CredentialUnavailableException or Azure.Identity.AuthenticationFailedException or HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            _logger.LogWarning(ex, "Could not inspect Logic App connections for {Setting}", workflowSetting);
            return "Connection diagnostics could not be completed. Ask an administrator to check the Logic App run history, API connections, and the app identity's read permissions.";
        }
    }
}
