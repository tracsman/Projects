# Offline payload contract checks. Invoke-RestMethod is mocked; no email is sent.
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..'
$source = Get-Content (Join-Path $root 'Services\LogicAppService.cs') -Raw
$method = [regex]::Match($source, '(?s)public async Task<\(bool Success, bool DeliveryUnconfirmed, string\? Error, string\? ResponseBody\)> SendNotificationEmailAsync\(.*?\n    }').Value
$payload = [regex]::Match($method, '(?s)var payload = (?<value>new\s*\{.*?\});').Groups['value'].Value
if (-not $payload) { throw 'Email payload expression not found.' }
$code = @"
using System;
using System.Text.Json;
public static class EmailPayloadTest {
	private sealed class Tenant { public string Contacts => "test@example.invalid"; public string TenantName => "Test tenant"; }
	public static string Serialize() {
		var tenant = new Tenant(); var subject = "Test subject"; var htmlBody = "<p>Test body</p>"; var requestedBy = "Tester";
		var payload = $payload;
		return JsonSerializer.Serialize(payload);
	}
}
"@
Add-Type -TypeDefinition $code
$body = [EmailPayloadTest]::Serialize() | ConvertFrom-Json
if ($body.inlineAttachments -isnot [array] -or $body.inlineAttachments.Count -ne 0) { throw 'PathWeb must serialize inlineAttachments as an empty JSON array.' }
if ($body.to -ne 'test@example.invalid' -or $body.subject -ne 'Test subject' -or $body.htmlBody -ne '<p>Test body</p>' -or $body.tenantName -ne 'Test tenant' -or $body.requestedBy -ne 'Tester') { throw 'Email payload fields changed unexpectedly.' }
$definition = Get-Content (Join-Path $root 'LogicApp\PathWeb-Email.definition.json') -Raw | ConvertFrom-Json
$expression = $definition.actions.Send_an_email_V2.inputs.body.Attachments
if ($expression -ne "@coalesce(triggerBody()?['inlineAttachments'], json('[]'))") { throw 'Workflow must default absent attachments to an empty array while preserving supplied arrays.' }
$capture = @{ Body = $null }
function Invoke-RestMethod {
	param($Uri, $Method, $ContentType, $Body)
	$capture.Body = $Body | ConvertFrom-Json
	[pscustomobject]@{ status = 'sent' }
}
& (Join-Path $root 'test-email-logic-app.ps1') -TriggerUrl 'https://example.invalid/test' -To 'test@example.invalid' -HtmlBody '<p>Test body</p>' | Out-Null
if ($capture.Body.inlineAttachments -isnot [array] -or $capture.Body.inlineAttachments.Count -ne 0) { throw 'Standalone test must send an empty JSON attachment array.' }
Write-Output 'Email attachment checks passed: PathWeb payload, standalone payload, and workflow default.'
