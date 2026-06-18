#!/usr/bin/env pwsh
#Requires -Version 7.0
#Requires -Modules Az.Accounts, Az.Websites

<#
.SYNOPSIS
    Quick deploy PathWeb to Azure App Service
.DESCRIPTION
    Skips all Azure resource provisioning and configuration.
    Just builds, packages, and pushes the app to the existing Azure App Service.
    Use the full deploy-to-azure.ps1 script if Azure resources need to be created or reconfigured.
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "labinfrastructure",

    [Parameter(Mandatory = $false)]
    [string]$WebAppName = "PathWeb"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step  { param([string]$Message) Write-Host "→ $Message" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "✓ $Message" -ForegroundColor Green }
function Write-Error { param([string]$Message) Write-Host "✗ $Message" -ForegroundColor Red }
function Write-Info  { param([string]$Message) Write-Host "ℹ $Message" -ForegroundColor Yellow }

$projectPath = "Q:\Bin\git\Projects\PathWeb"
$publishPath = Join-Path $projectPath "publish"
$zipPath     = Join-Path $projectPath "publish.zip"
$projectFile = Join-Path $projectPath "PathWeb.csproj"

Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  PathWeb Quick Deploy" -ForegroundColor Magenta
Write-Host "========================================`n" -ForegroundColor Magenta

# Verify Azure login
Write-Step "Checking Azure login..."
try {
    $context = Get-AzContext -ErrorAction Stop
    if ($null -eq $context -or $null -eq $context.Account) { throw "Not logged in" }
    Write-Success "Logged in as $($context.Account.Id)"
} catch {
    Write-Error "Not logged in. Run Connect-AzAccount first."
    exit 1
}

# Clean and publish
Write-Step "Publishing application..."
Push-Location $projectPath
try {
    dotnet clean $projectFile -c Release --nologo -v q
    dotnet publish $projectFile -c Release -o $publishPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed!"
        Pop-Location
        exit 1
    }
} finally {
    Pop-Location
}

$dllPath = Join-Path $publishPath "PathWeb.dll"
$buildTime = (Get-Item $dllPath).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
Write-Success "Built: $buildTime"

# Write build info for verification
@{ build = $buildTime } | ConvertTo-Json | Set-Content -Path (Join-Path $publishPath "build.json") -Encoding UTF8

# Package
Write-Step "Packaging..."
Compress-Archive -Path "$publishPath\*" -DestinationPath $zipPath -Force
$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Success "Package: $zipSize MB"

# Deploy
Write-Step "Deploying to $WebAppName..."
try {
    Publish-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName -ArchivePath $zipPath -Force | out-null
    Write-Success "Deployed!"
} catch {
    Write-Error "Deploy failed: $_"
    exit 1
}

# Restart the app to ensure the new build is picked up immediately
$appUrl = "https://$WebAppName.azurewebsites.net"
Write-Step "Restarting $WebAppName..."
Restart-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName | Out-Null
Write-Success "Restart initiated"

# Wait for the NEW build to be live (compare build timestamps).
# Important: use a generous per-attempt timeout (90s) and let each call complete
# before issuing the next one. Earlier versions used a 30s timeout in a 5s loop,
# which on Basic SKU + cold SQL connection pool produced multiple overlapping
# /warmup calls (each runs ~30 EF queries), saturating the single worker and
# stalling even AllowAnonymous endpoints like /health for several minutes.
Write-Step "Waiting for new build ($buildTime) to come online..."
$ready = $false
$maxAttempts = 10
for ($i = 1; $i -le $maxAttempts; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "$appUrl/warmup" -TimeoutSec 90 -UseBasicParsing -ErrorAction Stop
        $warmup = $response.Content | ConvertFrom-Json
        if ($warmup.build -eq $buildTime) {
            Write-Host ""
            Write-Success "New build is live! (build: $($warmup.build), db: $($warmup.dbMs)ms, attempt $i)"
            $ready = $true
            break
        } else {
            # Old build is still answering; the swap hasn't completed yet.
            Write-Host "." -NoNewline
            Start-Sleep -Seconds 10
        }
    } catch {
        # Timeout or error - wait briefly before the next attempt so we never
        # have two overlapping /warmup requests against the cold worker.
        Write-Host "." -NoNewline
        Start-Sleep -Seconds 5
    }
}
if (-not $ready) {
    Write-Info "New build did not appear after $maxAttempts attempts (~15 min worst case). Current build may still be swapping in."
}

Write-Step "Compiling MVC/Razor views..."
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
Write-Success "All pages warmed up!"

# Done
Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  Quick Deploy Complete!" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  URL   : " -NoNewline; Write-Host $appUrl -ForegroundColor Cyan
Write-Host "  Build : $buildTime"
Write-Host "========================================`n" -ForegroundColor Magenta
