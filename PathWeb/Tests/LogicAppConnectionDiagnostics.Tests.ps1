# Offline tests: real diagnostic source, fake ARM responses and fake credential; no network calls.
$ErrorActionPreference = 'Stop'
$source = (Get-Content (Join-Path $PSScriptRoot '..\Services\LogicAppConnectionDiagnostics.cs') -Raw).Replace('namespace PathWeb.Services;', '')
$stubs = @'
namespace Azure {
	public class RequestFailedException : System.Exception { }
}
namespace Azure.Core {
	public class TokenRequestContext { public TokenRequestContext(string[] scopes) { } }
	public readonly struct AccessToken { public string Token { get; } public AccessToken(string token) { Token = token; } }
	public abstract class TokenCredential {
		public abstract System.Threading.Tasks.ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, System.Threading.CancellationToken cancellationToken);
	}
}
namespace Azure.Identity {
	public class CredentialUnavailableException : System.Exception { }
	public class AuthenticationFailedException : System.Exception { }
	public class DefaultAzureCredential : Azure.Core.TokenCredential {
		public override System.Threading.Tasks.ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext c, System.Threading.CancellationToken t) => new(new Azure.Core.AccessToken("fake"));
	}
}
public interface IConfiguration { string? this[string key] { get; } }
public interface IHttpClientFactory { System.Net.Http.HttpClient CreateClient(); }
public interface ILogger<T> { }
public static class LoggerExtensions {
	public static void LogWarning<T>(this ILogger<T> logger, string message, params object?[] args) { }
	public static void LogWarning<T>(this ILogger<T> logger, System.Exception error, string message, params object?[] args) { }
}
public sealed class FakeConfig : IConfiguration {
	public string? Value { get; set; }
	public string? this[string key] => Value;
}
public sealed class FakeLogger : ILogger<LogicAppConnectionDiagnostics> { }
public sealed class FakeCredential : Azure.Core.TokenCredential {
	public override System.Threading.Tasks.ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext c, System.Threading.CancellationToken t) => new(new Azure.Core.AccessToken("fake-token"));
}
public sealed class FakeHandler : System.Net.Http.HttpMessageHandler {
	public System.Collections.Generic.Queue<System.Net.Http.HttpResponseMessage> Responses = new();
	public System.Collections.Generic.List<string> Requests = new();
	protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken token) {
		Requests.Add(request.RequestUri!.ToString());
		if (request.Headers.Authorization?.Parameter != "fake-token") throw new System.InvalidOperationException("Missing token");
		return System.Threading.Tasks.Task.FromResult(Responses.Dequeue());
	}
	public void Add(int status, string body) => Responses.Enqueue(new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new System.Net.Http.StringContent(body) });
}
public sealed class FakeFactory : IHttpClientFactory {
	public FakeHandler Handler = new();
	public System.Net.Http.HttpClient CreateClient() => new(Handler, false);
}
'@
Add-Type -TypeDefinition ("#nullable enable`nglobal using System;`nglobal using System.Net.Http;`nglobal using System.Collections.Generic;`nglobal using System.Linq;`nglobal using System.Threading;`nglobal using System.Threading.Tasks;`n" + $source + "`n" + $stubs)
$workflowId = '/subscriptions/4bffbb15-d414-4874-a2e4-c548c6d45e2a/resourceGroups/labinfrastructure/providers/Microsoft.Logic/workflows/PathWeb-Email'
$connectionId = '/subscriptions/4bffbb15-d414-4874-a2e4-c548c6d45e2a/resourceGroups/labinfrastructure/providers/Microsoft.Web/connections/office365-2'
$config = [FakeConfig]::new()
$factory = [FakeFactory]::new()
$service = [LogicAppConnectionDiagnostics]::new($config, $factory, [FakeLogger]::new(), [FakeCredential]::new())
function Check { $service.CheckAsync('email').GetAwaiter().GetResult() }
function EnqueueWorkflow {
	$body = @{ properties = @{ parameters = @{ '$connections' = @{ value = @{ first = @{ connectionId = $connectionId }; duplicate = @{ connectionId = $connectionId } } } } } } | ConvertTo-Json -Depth 10 -Compress
	$factory.Handler.Add(200, $body)
}
if ((Check) -notmatch 'not configured') { throw 'Missing configuration was not reported.' }
$config.Value = 'https://example.invalid/'
if ((Check) -notmatch 'misconfigured' -or $factory.Handler.Requests.Count -ne 0) { throw 'Invalid workflow ID was accepted.' }
$config.Value = $workflowId
EnqueueWorkflow
$factory.Handler.Add(200, '{"properties":{"statuses":[{"status":"Error","error":{"code":"Unauthorized","message":"SECRET"}}]}}')
$auth = Check
if ($auth -notmatch "office365-2.*authentication problem.*reauthorize" -or $auth -match 'SECRET' -or $factory.Handler.Requests.Count -ne 2) { throw "Incorrect auth diagnostic: $auth" }
EnqueueWorkflow
$factory.Handler.Add(200, '{"properties":{"statuses":[{"status":"Connected"}]}}')
if ((Check) -notmatch 'currently report Connected') { throw 'Healthy connector result was incorrect.' }
EnqueueWorkflow
$factory.Handler.Add(200, '{"properties":{"statuses":[{"status":"Error","error":{"code":"Other"}}]}}')
$other = Check
if ($other -notmatch "office365-2.*status 'Error'" -or $other -match 'reauthorize') { throw "Non-auth error was misclassified: $other" }
EnqueueWorkflow
$factory.Handler.Add(403, '{}')
if ((Check) -notmatch 'could not be completed.*read permissions') { throw 'ARM access failure was not surfaced.' }
EnqueueWorkflow
$factory.Handler.Add(200, '{"properties":{"statuses":[]}}')
if ((Check) -notmatch 'could not be completed') { throw 'Missing status was not surfaced.' }
Write-Output 'Logic App connector diagnostics checks passed: authentication, healthy, other error, access failure, missing data, and configuration.'
