using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace PathWeb.Services;

public sealed class AutomationSubmitResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string RunbookName { get; set; } = string.Empty;
    public string PreparedScript { get; set; } = string.Empty;
}

public sealed class AutomationJobStatusResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string JobId { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown";
    public bool IsTerminal { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Exception { get; set; } = string.Empty;
}

public sealed class AutomationProbeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
}

public class AutomationService
{
    private static readonly string[] TerminalStatuses = ["Completed", "Failed", "Stopped", "Suspended"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AutomationService> _logger;
    private readonly AutomationOptions _options;
    private readonly TokenCredential _credential;

    public AutomationService(
        IHttpClientFactory httpClientFactory,
        IOptions<AutomationOptions> options,
        ILogger<AutomationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options.Value;
        _credential = new DefaultAzureCredential();
    }

    public bool IsConfigured => _options.IsConfigured;

    public string? GetConfigurationError()
    {
        if (_options.IsConfigured)
            return null;

        return "Automation settings are incomplete. Configure Automation:SubscriptionId, ResourceGroupName, AccountName, and Location.";
    }

    public string PrepareScriptForAutomation(string script)
    {
        var normalized = script.Replace("\r\n", "\n").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        if (normalized.Contains("Connect-AzAccount -Identity", StringComparison.OrdinalIgnoreCase))
            return normalized.Replace("\n", "\r\n");

        normalized = Regex.Replace(
            normalized,
            @"(?ms)try\s*\{\s*Get-AzContext.*?catch\s*\{.*?Connect-AzAccount.*?\}\s*",
            string.Empty,
            RegexOptions.IgnoreCase);

        normalized = Regex.Replace(
            normalized,
            @"(?ms)if\s*\(\s*-not\s*\(\s*Get-AzContext.*?\)\s*\)\s*\{.*?\}\s*",
            string.Empty,
            RegexOptions.IgnoreCase);

        normalized = "Connect-AzAccount -Identity | Out-Null\n\n" + normalized;
        return normalized.Replace("\n", "\r\n");
    }

    public async Task<AutomationSubmitResult> SubmitPowerShellRunbookAsync(string configType, string scriptContent, string submittedBy, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new AutomationSubmitResult
            {
                Success = false,
                Error = GetConfigurationError()
            };
        }

        var preparedScript = PrepareScriptForAutomation(scriptContent);
        if (string.IsNullOrWhiteSpace(preparedScript))
        {
            return new AutomationSubmitResult
            {
                Success = false,
                Error = "Script content is empty."
            };
        }

        var runbookName = BuildRunbookName(configType);
        var jobId = Guid.NewGuid().ToString();

        try
        {
            await CreateOrUpdateRunbookAsync(runbookName, configType, submittedBy, cancellationToken);
            await ReplaceDraftContentAsync(runbookName, preparedScript, cancellationToken);
            await PublishRunbookAsync(runbookName, cancellationToken);
            await WaitForRunbookPublishedAsync(runbookName, cancellationToken);
            await StartJobAsync(runbookName, jobId, cancellationToken);

            _logger.LogInformation("Automation runbook {RunbookName} submitted as job {JobId} for {ConfigType} by {User}",
                runbookName, jobId, configType, submittedBy);

            return new AutomationSubmitResult
            {
                Success = true,
                JobId = jobId,
                RunbookName = runbookName,
                PreparedScript = preparedScript
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit automation runbook for {ConfigType}", configType);
            return new AutomationSubmitResult
            {
                Success = false,
                Error = ex.Message,
                RunbookName = runbookName,
                PreparedScript = preparedScript
            };
        }
    }

    public async Task<bool> DeleteRunbookAsync(string runbookName, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(runbookName))
            return false;

        try
        {
            using var response = await SendRawAsync(
                HttpMethod.Delete,
                BuildAccountUrl($"runbooks/{Uri.EscapeDataString(runbookName)}"),
                null,
                cancellationToken);

            if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogInformation("Automation runbook {RunbookName} cleanup {Result}",
                    runbookName,
                    response.StatusCode == System.Net.HttpStatusCode.NotFound ? "skipped (already deleted)" : "completed");
                return true;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("Failed to delete automation runbook {RunbookName}: {StatusCode} {Body}",
                runbookName, response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to clean up automation runbook {RunbookName}", runbookName);
            return false;
        }
    }

    public async Task<AutomationProbeResult> ProbeAccountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new AutomationProbeResult
            {
                Success = false,
                Error = GetConfigurationError(),
                AccountName = _options.AccountName,
                Location = _options.Location
            };
        }

        try
        {
            var account = await SendAndReadJsonAsync<AutomationAccountResponse>(
                HttpMethod.Get,
                BuildAccountUrl(),
                cancellationToken: cancellationToken);

            return new AutomationProbeResult
            {
                Success = true,
                AccountName = account?.Name ?? _options.AccountName,
                Location = account?.Location ?? _options.Location
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe automation account {AccountName}", _options.AccountName);
            return new AutomationProbeResult
            {
                Success = false,
                Error = ex.Message,
                AccountName = _options.AccountName,
                Location = _options.Location
            };
        }
    }

    public async Task<AutomationJobStatusResult> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return new AutomationJobStatusResult
            {
                Success = false,
                Error = GetConfigurationError() ?? "Automation is not configured.",
                JobId = jobId
            };
        }

        try
        {
            var job = await SendAndReadJsonAsync<JobResponse>(
                HttpMethod.Get,
                BuildAccountUrl($"jobs/{Uri.EscapeDataString(jobId)}"),
                cancellationToken: cancellationToken);

            var status = job?.Properties?.Status ?? "Unknown";
            var output = await TryGetJobOutputAsync(jobId, cancellationToken) ?? string.Empty;
            var exception = job?.Properties?.Exception ?? string.Empty;

            return new AutomationJobStatusResult
            {
                Success = true,
                JobId = jobId,
                Status = status,
                IsTerminal = TerminalStatuses.Contains(status, StringComparer.OrdinalIgnoreCase),
                Output = output,
                Exception = exception
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get automation job status for {JobId}", jobId);
            return new AutomationJobStatusResult
            {
                Success = false,
                Error = ex.Message,
                JobId = jobId
            };
        }
    }

    private async Task CreateOrUpdateRunbookAsync(string runbookName, string configType, string submittedBy, CancellationToken cancellationToken)
    {
        var body = new
        {
            location = _options.Location,
            properties = new
            {
                runbookType = "PowerShell",
                logProgress = true,
                logVerbose = true,
                description = $"PathWeb submitted runbook for {configType} by {submittedBy} at {DateTime.UtcNow:u}"
            }
        };

        await SendAsync(HttpMethod.Put, BuildAccountUrl($"runbooks/{Uri.EscapeDataString(runbookName)}"), body, cancellationToken);
    }

    private async Task ReplaceDraftContentAsync(string runbookName, string scriptContent, CancellationToken cancellationToken)
    {
        var url = BuildAccountUrl($"runbooks/{Uri.EscapeDataString(runbookName)}/draft/content");
        var content = new StringContent(scriptContent, Encoding.UTF8, "text/powershell");

        var response = await SendRawAsync(HttpMethod.Put, url, content, cancellationToken);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Runbook draft content update with text/powershell failed for {RunbookName}: {Status} {Body}",
            runbookName, response.StatusCode, body);

        response.Dispose();

        var fallbackContent = new StringContent(scriptContent, Encoding.UTF8, "text/plain");
        await EnsureSuccessAsync(await SendRawAsync(HttpMethod.Put, url, fallbackContent, cancellationToken), cancellationToken);
    }

    private async Task PublishRunbookAsync(string runbookName, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(await SendRawAsync(HttpMethod.Post, BuildAccountUrl($"runbooks/{Uri.EscapeDataString(runbookName)}/publish"), null, cancellationToken), cancellationToken);
    }

    private async Task WaitForRunbookPublishedAsync(string runbookName, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 15; attempt++)
        {
            var runbook = await SendAndReadJsonAsync<RunbookResponse>(
                HttpMethod.Get,
                BuildAccountUrl($"runbooks/{Uri.EscapeDataString(runbookName)}"),
                cancellationToken: cancellationToken);

            var state = runbook?.Properties?.State;
            if (string.Equals(state, "Published", StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new InvalidOperationException($"Runbook '{runbookName}' did not reach Published state in time.");
    }

    private async Task StartJobAsync(string runbookName, string jobId, CancellationToken cancellationToken)
    {
        var body = new
        {
            properties = new
            {
                runbook = new { name = runbookName },
                parameters = new { }
            }
        };

        await SendAsync(HttpMethod.Put, BuildAccountUrl($"jobs/{Uri.EscapeDataString(jobId)}"), body, cancellationToken);
    }

    private async Task<string?> TryGetJobOutputAsync(string jobId, CancellationToken cancellationToken)
    {
        var response = await SendRawAsync(HttpMethod.Get, BuildAccountUrl($"jobs/{Uri.EscapeDataString(jobId)}/output"), null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        var text = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (!text.StartsWith("{", StringComparison.Ordinal) && !text.StartsWith("[", StringComparison.Ordinal))
            return text;

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("value", out var valueProp) && valueProp.ValueKind == JsonValueKind.String)
                    return valueProp.GetString();
                if (doc.RootElement.TryGetProperty("properties", out var propertiesProp))
                {
                    if (propertiesProp.TryGetProperty("output", out var outputProp) && outputProp.ValueKind == JsonValueKind.String)
                        return outputProp.GetString();
                    if (propertiesProp.TryGetProperty("summary", out var summaryProp) && summaryProp.ValueKind == JsonValueKind.String)
                        return summaryProp.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Fall through to raw text.
        }

        return text;
    }

    private async Task<T?> SendAndReadJsonAsync<T>(HttpMethod method, string url, object? body = null, CancellationToken cancellationToken = default)
    {
        using var response = await SendRawAsync(method, url, body is null ? null : JsonContent(body), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return default;
        return JsonSerializer.Deserialize<T>(text, JsonOptions);
    }

    private async Task SendAsync(HttpMethod method, string url, object body, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(method, url, JsonContent(body), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient();
        var token = await _credential.GetTokenAsync(
            new TokenRequestContext(["https://management.azure.com/.default"]),
            cancellationToken);

        var request = new HttpRequestMessage(method, url)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        return await client.SendAsync(request, cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"Azure Automation API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
    }

    private static StringContent JsonContent(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private string BuildAccountUrl(string? relativePath = null)
    {
        var accountPath = $"https://management.azure.com/subscriptions/{_options.SubscriptionId}/resourceGroups/{_options.ResourceGroupName}/providers/Microsoft.Automation/automationAccounts/{_options.AccountName}";
        if (string.IsNullOrWhiteSpace(relativePath))
            return $"{accountPath}?api-version={_options.ApiVersion}";

        return $"{accountPath}/{relativePath}?api-version={_options.ApiVersion}";
    }

    private static string BuildRunbookName(string configType)
    {
        var safeType = Regex.Replace(configType, "[^A-Za-z0-9-]", "-");
        var suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var runbookName = $"pathweb-{safeType}-{suffix}".ToLowerInvariant();
        return runbookName.Length <= 63 ? runbookName : runbookName[..63];
    }

    private sealed class RunbookResponse
    {
        public RunbookProperties? Properties { get; set; }
    }

    private sealed class RunbookProperties
    {
        public string? State { get; set; }
    }

    private sealed class JobResponse
    {
        public JobProperties? Properties { get; set; }
    }

    private sealed class AutomationAccountResponse
    {
        public string? Name { get; set; }
        public string? Location { get; set; }
    }

    private sealed class JobProperties
    {
        public string? Status { get; set; }
        public string? Exception { get; set; }
    }
}
