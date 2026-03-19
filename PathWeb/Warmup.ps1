#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Warm up PathWeb pages to force JIT compilation
.DESCRIPTION
    Polls the unauthenticated /warmup endpoint to pre-compile EF Core and the DI pipeline,
    then touches auth-protected routes to compile their Razor Pages (Easy Auth will redirect,
    but the ASP.NET middleware still JIT-compiles the page handler).
    Use this after App Service restarts, scales out, or any time the site feels cold.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$WebAppName = "PathWeb"
)

$appUrl = "https://$WebAppName.azurewebsites.net"

Write-Host "`nWarming up $appUrl ...`n" -ForegroundColor Cyan

# 1. Poll /warmup until EF Core, DI, and SQL are ready
Write-Host "→ Warming up EF Core and DI pipeline..." -ForegroundColor Cyan
$ready = $false
for ($i = 1; $i -le 36; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "$appUrl/warmup" -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop
        $warmup = $response.Content | ConvertFrom-Json
        Write-Host "✓ App is warm (build: $($warmup.build), db: $($warmup.dbMs)ms)" -ForegroundColor Green
        $ready = $true
        break
    } catch {
        Write-Host "." -NoNewline
        $script:lastError = $_.Exception.Message
        Start-Sleep -Seconds 5
    }
}
if (-not $ready) {
    Write-Host ""
    Write-Host "ℹ App did not respond to /warmup after 3 minutes" -ForegroundColor Yellow
    Write-Host "  Last error: $lastError" -ForegroundColor Yellow
}

# 2. Touch auth-protected routes to JIT-compile MVC/Razor views
Write-Host "→ Compiling MVC/Razor views..." -ForegroundColor Cyan
$warmupPaths = @("/", "/Tenants", "/Addresses", "/Devices",
                  "/Users", "/ToolTips", "/Logs",
                  "/Requests/Queue", "/Settings", "/diag/view",
                  "/About", "/About/Lab", "/About/Tenant", "/About/Progress")
foreach ($path in $warmupPaths) {
    try {
        Invoke-WebRequest -Uri "$appUrl$path" -TimeoutSec 15 -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue | Out-Null
    } catch { }
    Write-Host "  $path" -ForegroundColor DarkGray
}
Write-Host "✓ All pages warmed up!" -ForegroundColor Green

Write-Host "`n✓ Warmup complete!`n" -ForegroundColor Green
