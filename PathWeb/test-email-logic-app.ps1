#!/usr/bin/env pwsh
#Requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TriggerUrl,

    [Parameter(Mandatory)]
    [string]$To,

    [string]$Subject = "PathWeb notification email test",
    [string]$TenantName = "PathWeb Test Tenant",
    [string]$RequestedBy = "PathWeb test harness",
    [string]$HtmlBody
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step    { param([string]$Message) Write-Host "`u{2192} $Message" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "`u{2713} $Message" -ForegroundColor Green }
function Write-Info    { param([string]$Message) Write-Host "`u{2139} $Message" -ForegroundColor Yellow }

if ([string]::IsNullOrWhiteSpace($HtmlBody)) {
    $HtmlBody = @"
<!DOCTYPE html>
<html>
<head>
    <title>$Subject</title>
</head>
<body style="font-family:Calibri,sans-serif;font-size:12pt;">
    <h2>PathWeb notification email test</h2>
    <p>This is a test of the dedicated mailbox Logic App path.</p>
    <table style="border-collapse:collapse;">
        <tr><td style="padding:4px 12px 4px 0;"><strong>Tenant</strong></td><td>$TenantName</td></tr>
        <tr><td style="padding:4px 12px 4px 0;"><strong>Requested By</strong></td><td>$RequestedBy</td></tr>
        <tr><td style="padding:4px 12px 4px 0;"><strong>Generated</strong></td><td>$(Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz")</td></tr>
    </table>
    <p>If this renders correctly, the Logic App email path is ready for app integration.</p>
</body>
</html>
"@
}

$payload = @{
    to = $To
    subject = $Subject
    htmlBody = $HtmlBody
    tenantName = $TenantName
    requestedBy = $RequestedBy
} | ConvertTo-Json -Depth 10

Write-Step "Posting test email payload to Logic App..."
$response = Invoke-RestMethod -Uri $TriggerUrl -Method Post -ContentType "application/json" -Body $payload

Write-Success "Logic App call completed"
$response | ConvertTo-Json -Depth 10
Write-Host ""
Write-Info "Check the destination mailbox for delivery and HTML rendering."
