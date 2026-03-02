#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Warm up PathWeb pages to force JIT compilation
.DESCRIPTION
    Hits all major routes on the deployed Azure App Service to pre-compile pages.
    Easy Auth redirects are expected and silently ignored.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$WebAppName = "testPathWeb-app"
)

$appUrl = "https://$WebAppName.azurewebsites.net"

Write-Host "`nWarming up $appUrl ...`n" -ForegroundColor Cyan

$warmupPaths = @(
    "/",
    "/Tenants",
    "/Addresses",
    "/Users",
    "/About",
    "/About/Lab",
    "/About/Tenant",
    "/About/Progress"
)

foreach ($path in $warmupPaths) {
    try {
        Invoke-WebRequest -Uri "$appUrl$path" -TimeoutSec 15 -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue | Out-Null
    } catch {
        # Ignore - Easy Auth redirects are expected
    }
    Write-Host "  $path" -ForegroundColor DarkGray
}

Write-Host "`n✓ All pages warmed up!`n" -ForegroundColor Green
