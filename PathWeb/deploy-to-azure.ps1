#!/usr/bin/env pwsh
#Requires -Version 7.0
#Requires -Modules Az.Accounts, Az.Websites, Az.Resources

<#
.SYNOPSIS
    Deploy PathWeb application to Azure App Service
.DESCRIPTION
    Creates Azure resources and deploys the PathWeb .NET 10 application to Azure App Service
    Uses Azure PowerShell cmdlets (Az module)
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ResourceGroup = "testPathWeb",

    [Parameter(Mandatory = $false)]
    [string]$Location = "westus2",

    [Parameter(Mandatory = $false)]
    [string]$AppServicePlan = "testPathWeb-app-plan",

    [Parameter(Mandatory = $false)]
    [string]$WebAppName = "testPathWeb-app", #-$(Get-Random -Minimum 1000 -Maximum 9999)",

    [Parameter(Mandatory = $false)]
    [ValidateSet("B1", "B2", "B3", "S1", "S2", "S3", "P1V2", "P2V2", "P3V2")]
    [string]$Sku = "B1"
)

# Set strict mode
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Colors for output
function Write-Step {
    param([string]$Message)
    Write-Host "→ $Message" -ForegroundColor Cyan
}

function Write-Success {
    param([string]$Message)
    Write-Host "✓ $Message" -ForegroundColor Green
}

function Write-Error {
    param([string]$Message)
    Write-Host "✗ $Message" -ForegroundColor Red
}

function Write-Info {
    param([string]$Message)
    Write-Host "ℹ $Message" -ForegroundColor Yellow
}

# Start deployment
Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  PathWeb Azure Deployment Script" -ForegroundColor Magenta
Write-Host "========================================`n" -ForegroundColor Magenta

# Display configuration
Write-Info "Configuration:"
Write-Host "  Resource Group    : $ResourceGroup"
Write-Host "  Location          : $Location"
Write-Host "  App Service Plan  : $AppServicePlan"
Write-Host "  Web App Name      : $WebAppName"
Write-Host "  SKU               : $Sku"
Write-Host ""

# Check if Azure PowerShell modules are installed
Write-Step "Checking Azure PowerShell modules..."
$requiredModules = @('Az.Accounts', 'Az.Websites', 'Az.Resources')
$missingModules = @()

foreach ($module in $requiredModules) {
    if (-not (Get-Module -ListAvailable -Name $module)) {
        $missingModules += $module
    }
}

if ($missingModules.Count -gt 0) {
    Write-Error "Missing required Azure PowerShell modules: $($missingModules -join ', ')"
    Write-Info "Install with: Install-Module -Name Az -Scope CurrentUser -Repository PSGallery -Force"
    exit 1
}
Write-Success "All required Azure PowerShell modules found"

# Check if logged in
Write-Step "Checking Azure login status..."
try {
    $context = Get-AzContext -ErrorAction Stop
    if ($null -eq $context -or $null -eq $context.Account) {
        throw "Not logged in"
    }
    Write-Success "Logged in as $($context.Account.Id) - Subscription: $($context.Subscription.Name)"
} catch {
    Write-Info "Not logged in to Azure. Starting login process..."
    try {
        Connect-AzAccount -ErrorAction Stop
        $context = Get-AzContext
        Write-Success "Successfully logged in as $($context.Account.Id)"
    } catch {
        Write-Error "Azure login failed: $_"
        exit 1
    }
}

# Create Resource Group
Write-Step "Creating resource group '$ResourceGroup'..."
try {
    $rg = Get-AzResourceGroup -Name $ResourceGroup -ErrorAction SilentlyContinue
    if ($rg) {
        Write-Info "Resource group already exists, skipping creation"
    } else {
        $rg = New-AzResourceGroup -Name $ResourceGroup -Location $Location
        Write-Success "Resource group created in $Location"
    }
} catch {
    Write-Error "Failed to create resource group: $_"
    exit 1
}

# Create App Service Plan
Write-Step "Creating App Service Plan '$AppServicePlan'..."

# Map SKU to Tier and WorkerSize
$skuMapping = @{
    'B1' = @{ Tier = 'Basic'; WorkerSize = 'Small' }
    'B2' = @{ Tier = 'Basic'; WorkerSize = 'Medium' }
    'B3' = @{ Tier = 'Basic'; WorkerSize = 'Large' }
    'S1' = @{ Tier = 'Standard'; WorkerSize = 'Small' }
    'S2' = @{ Tier = 'Standard'; WorkerSize = 'Medium' }
    'S3' = @{ Tier = 'Standard'; WorkerSize = 'Large' }
    'P1V2' = @{ Tier = 'PremiumV2'; WorkerSize = 'Small' }
    'P2V2' = @{ Tier = 'PremiumV2'; WorkerSize = 'Medium' }
    'P3V2' = @{ Tier = 'PremiumV2'; WorkerSize = 'Large' }
}

$tier = $skuMapping[$Sku].Tier
$workerSize = $skuMapping[$Sku].WorkerSize

try {
    $plan = Get-AzAppServicePlan -ResourceGroupName $ResourceGroup -Name $AppServicePlan -ErrorAction SilentlyContinue
    if ($plan) {
        Write-Info "App Service Plan already exists, skipping creation"
    } else {
        $plan = New-AzAppServicePlan `
            -ResourceGroupName $ResourceGroup `
            -Name $AppServicePlan `
            -Location $Location `
            -Tier $tier `
            -WorkerSize $workerSize `
            -Linux

        Write-Success "App Service Plan created with $Sku tier ($tier / $workerSize)"
    }
} catch {
    Write-Error "Failed to create App Service Plan: $_"
    exit 1
}

# Check if web app exists
Write-Step "Checking if Web App '$WebAppName' exists..."
try {
    $existingApp = Get-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName -ErrorAction SilentlyContinue
    if ($existingApp) {
        Write-Info "Web App already exists, will update existing app"
        $webAppExists = $true
    } else {
        Write-Success "Web app name '$WebAppName' is available"
        $webAppExists = $false
    }
} catch {
    # Name is available (exception means not found)
    Write-Success "Web app name '$WebAppName' is available"
    $webAppExists = $false
}

# Create Web App (only if it doesn't exist)
if (-not $webAppExists) {
    Write-Step "Creating Web App '$WebAppName'..."
    try {
        $webApp = New-AzWebApp `
            -ResourceGroupName $ResourceGroup `
            -Name $WebAppName `
            -Location $Location `
            -AppServicePlan $AppServicePlan

        Write-Success "Web App created"
    } catch {
        Write-Error "Failed to create Web App: $_"
        exit 1
    }
} else {
    Write-Info "Skipping Web App creation (already exists)"
}

# Configure HTTPS only
Write-Step "Configuring HTTPS-only access..."
try {
    $currentWebApp = Get-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName

    if ($currentWebApp.HttpsOnly) {
        Write-Info "HTTPS-only is already enabled, skipping configuration"
    } else {
        Set-AzWebApp `
            -ResourceGroupName $ResourceGroup `
            -Name $WebAppName `
            -HttpsOnly $true

        Write-Success "HTTPS-only enabled"
    }
} catch {
    Write-Error "Failed to configure HTTPS: $_"
}

# Configure Linux runtime stack for .NET 10
Write-Step "Configuring .NET 10 runtime stack..."
try {
    $webAppResourceId = "/subscriptions/$($context.Subscription.Id)/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$WebAppName/config/web"

    $properties = @{
        linuxFxVersion = "DOTNETCORE|10.0"
    }

    Set-AzResource `
        -ResourceId $webAppResourceId `
        -Properties $properties `
        -Force

    Write-Success ".NET 10 runtime configured"
} catch {
    Write-Error "Failed to configure runtime: $_"
    Write-Info "Attempting alternative configuration method..."

    # Alternative: Use REST API via Invoke-AzRestMethod
    try {
        $uri = "/subscriptions/$($context.Subscription.Id)/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$WebAppName/config/web?api-version=2022-03-01"
        $body = @{
            properties = @{
                linuxFxVersion = "DOTNETCORE|10.0"
            }
        } | ConvertTo-Json

        Invoke-AzRestMethod `
            -Path $uri `
            -Method PUT `
            -Payload $body

        Write-Success ".NET 10 runtime configured (via REST API)"
    } catch {
        Write-Error "All configuration methods failed. You may need to configure the runtime manually in the portal."
    }
}

# Exclude /health from Easy Auth so deploy script can poll it
Write-Step "Configuring Easy Auth exclusion for /health endpoint..."
try {
    $authUri = "/subscriptions/$($context.Subscription.Id)/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$WebAppName/config/authsettingsV2?api-version=2022-03-01"

    # Get current auth config
    $authResponse = Invoke-AzRestMethod -Path $authUri -Method GET
    if ($authResponse.StatusCode -eq 200) {
        $authConfig = $authResponse.Content | ConvertFrom-Json

        # Check if /health is already excluded
        $excludedPaths = $authConfig.properties.globalValidation.excludedPaths
        if ($null -eq $excludedPaths -or "/health" -notin $excludedPaths) {
            if ($null -eq $excludedPaths) { $excludedPaths = @() }
            $excludedPaths = @($excludedPaths) + @("/health")

            $authBody = @{
                properties = @{
                    globalValidation = @{
                        excludedPaths = $excludedPaths
                    }
                }
            } | ConvertTo-Json -Depth 5

            Invoke-AzRestMethod -Path $authUri -Method PATCH -Payload $authBody | Out-Null
            Write-Success "/health excluded from Easy Auth"
        } else {
            Write-Info "/health already excluded from Easy Auth"
        }
    }
} catch {
    Write-Info "Could not configure Easy Auth exclusion: $_"
}

# Enable App Service application logging
Write-Step "Configuring application logging..."
try {
    $logsUri = "/subscriptions/$($context.Subscription.Id)/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$WebAppName/config/logs?api-version=2022-03-01"
    $logsBody = @{
        properties = @{
            applicationLogs = @{
                fileSystem = @{
                    level = "Information"
                }
            }
            httpLogs = @{
                fileSystem = @{
                    enabled = $true
                    retentionInDays = 7
                    retentionInMb = 35
                }
            }
        }
    } | ConvertTo-Json -Depth 5

    Invoke-AzRestMethod -Path $logsUri -Method PUT -Payload $logsBody | Out-Null
    Write-Success "Application logging enabled (Information level, 7-day retention)"
} catch {
    Write-Info "Could not configure logging: $_"
}

# Build and publish application
Write-Step "Building application..."
$projectPath = "Q:\Bin\git\Projects\PathWeb"
$publishPath = Join-Path $projectPath "publish"
$zipPath = Join-Path $projectPath "publish.zip"

# Change to project directory
Push-Location $projectPath

try {
    # Find the .csproj file
    Write-Step "Locating project file..."
    $projectFiles = @(Get-ChildItem -Path $projectPath -Filter "*.csproj" -File)

    if ($projectFiles.Count -eq 0) {
        Write-Error "No .csproj file found in $projectPath"
        Pop-Location
        exit 1
    }

    if ($projectFiles.Count -gt 1) {
        Write-Info "Multiple .csproj files found. Using: $($projectFiles[0].Name)"
    }

    $projectFile = $projectFiles[0].FullName
    Write-Success "Found project file: $($projectFiles[0].Name)"

    # Clean previous publish folder
    if (Test-Path $publishPath) {
        Remove-Item $publishPath -Recurse -Force
        Write-Info "Cleaned previous publish folder"
    }

    if (Test-Path $zipPath) {
        Remove-Item $zipPath -Force
        Write-Info "Cleaned previous publish zip"
    }

    # Clean and publish the application (clean ensures fresh build timestamp)
    Write-Step "Publishing application..."
    dotnet clean $projectFile -c Release --nologo -v q
    dotnet publish $projectFile -c Release -o $publishPath

    if ($LASTEXITCODE -eq 0) {
        Write-Success "Application built successfully"
        $dllPath = Join-Path $publishPath "PathWeb.dll"
        if (Test-Path $dllPath) {
            $buildTime = (Get-Item $dllPath).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
            Write-Info "Build timestamp: $buildTime"
        }

        # Write build info to a static file for deployment verification
        $buildInfo = @{ build = $buildTime } | ConvertTo-Json
        Set-Content -Path (Join-Path $publishPath "build.json") -Value $buildInfo -Encoding UTF8
    } else {
        Write-Error "Failed to build application"
        Pop-Location
        exit 1
    }

    # Create deployment zip
    Write-Step "Creating deployment package..."
    Compress-Archive -Path "$publishPath\*" -DestinationPath $zipPath -Force
    
    if (Test-Path $zipPath) {
        $zipSize = (Get-Item $zipPath).Length / 1MB
        Write-Success "Deployment package created ($([math]::Round($zipSize, 2)) MB)"
    } else {
        Write-Error "Failed to create deployment package"
        Pop-Location
        exit 1
    }

    # Deploy to Azure
    Write-Step "Deploying application to Azure..."
    Write-Info "This may take a few minutes..."

    try {
        Publish-AzWebApp `
            -ResourceGroupName $ResourceGroup `
            -Name $WebAppName `
            -ArchivePath $zipPath `
            -Force

        Write-Success "Application deployed successfully"
    } catch {
        Write-Error "Failed to deploy application: $_"
        Pop-Location
        exit 1
    }

} finally {
    Pop-Location
}

# App URL
$appUrl = "https://$WebAppName.azurewebsites.net"

# Summary
Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  Deployment Complete!" -ForegroundColor Magenta
Write-Host "========================================`n" -ForegroundColor Magenta

Write-Success "PathWeb has been successfully deployed to Azure!"
Write-Host ""
Write-Host "Application URL    : " -NoNewline
Write-Host $appUrl -ForegroundColor Cyan
Write-Host "Resource Group     : $ResourceGroup"
Write-Host "Web App Name       : $WebAppName"
Write-Host "Location           : $Location"
if ($buildTime) {
    Write-Host "Build Timestamp    : $buildTime"
}
Write-Host ""

Write-Info "Next Steps:"
Write-Host "  Browse to: $appUrl"
Write-Host ""

Write-Info "Useful PowerShell Commands:"
Write-Host "  View logs    : Get-AzWebAppPublishingProfile -ResourceGroupName $ResourceGroup -Name $WebAppName"
Write-Host "  Stop app     : Stop-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName"
Write-Host "  Start app    : Start-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName"
Write-Host "  Restart app  : Restart-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName"
Write-Host "  Get app info : Get-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName"
Write-Host "  Delete all   : Remove-AzResourceGroup -Name $ResourceGroup -Force"
Write-Host "Azure Portal:"
Write-Host "  View logs    : Azure Portal → App Service → Log stream"
Write-Host ""

# Open browser
$openBrowser = Read-Host "Would you like to open the app in your browser now? (Y/N)"
if ($openBrowser -eq 'Y' -or $openBrowser -eq 'y') {
    Write-Step "Opening browser..."
    Start-Process $appUrl
}

Write-Host "`nDeployment script completed!`n" -ForegroundColor Green
