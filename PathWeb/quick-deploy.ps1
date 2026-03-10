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

# Wait for the NEW build to be live (compare build timestamps)
Write-Step "Waiting for new build ($buildTime) to come online..."
$ready = $false
for ($i = 1; $i -le 60; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "$appUrl/warmup" -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop
        $warmup = $response.Content | ConvertFrom-Json
        if ($warmup.build -eq $buildTime) {
            Write-Host ""
            Write-Success "New build is live! (build: $($warmup.build), db: $($warmup.dbMs)ms)"
            $ready = $true
            break
        } else {
            Write-Host "." -NoNewline
            Start-Sleep -Seconds 5
        }
    } catch {
        Write-Host "." -NoNewline
        Start-Sleep -Seconds 5
    }
}
if (-not $ready) {
    Write-Info "New build did not appear after 5 minutes. Current build may still be swapping in."
}

Write-Step "Compiling Razor Pages..."
$warmupPaths = @("/", "/Tenants", "/Addresses", "/Devices",
                  "/Users", "/ToolTips",
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
