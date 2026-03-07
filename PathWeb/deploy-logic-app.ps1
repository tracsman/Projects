#!/usr/bin/env pwsh
#Requires -Version 7.0
#Requires -Modules Az.Accounts, Az.Resources

<#
.SYNOPSIS
    Deploy the PathWeb-ADO Logic App for Azure DevOps work item integration
.DESCRIPTION
    Creates (or recreates) the PathWeb-ADO Logic App from the exported definition in
    LogicApp/PathWeb-ADO.definition.json. This is the exact working definition exported
    from the production Logic App, including Create/Update work item actions with
    discussion comments.

    The Logic App uses the Azure DevOps managed connector authenticated with Entra ID
    credentials (your identity). After deployment of a NEW connection, you must authorize
    it via the Logic App designer in the Azure Portal.
.NOTES
    For a fresh deployment (new connection):
    1. Run this script
    2. Go to Azure Portal → Logic App → PathWeb-ADO → Logic App Designer
    3. Click on a red/warning ADO action → Sign in with your Entra ID credentials
    4. Save the Logic App in the designer
    5. Set the trigger URL: the script outputs the command

    For redeployment (connection already authorized):
    1. Run this script — it reuses the existing connection, no re-authorization needed
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup = "labinfrastructure",
    [string]$Location = "westus2",
    [string]$LogicAppName = "PathWeb-ADO",
    [string]$ConnectionName = "visualstudioteamservices-2",
    [string]$WebAppName = "PathWeb"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Step    { param([string]$Message) Write-Host "`u{2192} $Message" -ForegroundColor Cyan }
function Write-Success { param([string]$Message) Write-Host "`u{2713} $Message" -ForegroundColor Green }
function Write-Info    { param([string]$Message) Write-Host "`u{2139} $Message" -ForegroundColor Yellow }

Write-Host "`n========================================" -ForegroundColor Magenta
Write-Host "  PathWeb Logic App Deployment" -ForegroundColor Magenta
Write-Host "========================================`n" -ForegroundColor Magenta

# Check login
Write-Step "Checking Azure login..."
$context = Get-AzContext -ErrorAction Stop
if ($null -eq $context -or $null -eq $context.Account) {
    Connect-AzAccount -ErrorAction Stop
    $context = Get-AzContext
}
Write-Success "Logged in as $($context.Account.Id)"

$subscriptionId = $context.Subscription.Id

# Resolve script directory and definition file
$scriptDir = $PSScriptRoot
$definitionFile = Join-Path $scriptDir "LogicApp" "PathWeb-ADO.definition.json"
if (-not (Test-Path $definitionFile)) {
    throw "Definition file not found: $definitionFile"
}
Write-Success "Definition file found: $definitionFile"

# Check/create the ADO API connection
Write-Step "Checking Azure DevOps API connection '$ConnectionName'..."
$connectionUri = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/connections/$ConnectionName`?api-version=2016-06-01"
$connResponse = Invoke-AzRestMethod -Path $connectionUri -Method GET

$needsAuth = $false
if ($connResponse.StatusCode -eq 200) {
    $connStatus = ($connResponse.Content | ConvertFrom-Json).properties.statuses[0].status
    if ($connStatus -eq "Connected") {
        Write-Success "API connection exists and is authorized"
    } else {
        Write-Info "API connection exists but status is: $connStatus (needs authorization)"
        $needsAuth = $true
    }
} else {
    Write-Step "Creating API connection '$ConnectionName'..."
    $apiId = "/subscriptions/$subscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/visualstudioteamservices"
    $connBody = @{
        location = $Location
        properties = @{
            api = @{ id = $apiId }
            displayName = "Azure DevOps (PathWeb)"
        }
    } | ConvertTo-Json -Depth 10

    Invoke-AzRestMethod -Path $connectionUri -Method PUT -Payload $connBody | Out-Null
    Write-Success "API connection created"
    $needsAuth = $true
}

# Deploy the Logic App with the exported definition
Write-Step "Deploying Logic App '$LogicAppName' from definition file..."

$definition = Get-Content $definitionFile -Raw | ConvertFrom-Json -AsHashtable

$connectionId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/connections/$ConnectionName"
$apiId = "/subscriptions/$subscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/visualstudioteamservices"

$logicAppBody = @{
    location = $Location
    properties = @{
        state = "Enabled"
        definition = $definition
        parameters = @{
            "`$connections" = @{
                value = @{
                    visualstudioteamservices = @{
                        connectionId = $connectionId
                        connectionName = $ConnectionName
                        id = $apiId
                    }
                    "visualstudioteamservices-1" = @{
                        connectionId = $connectionId
                        connectionName = $ConnectionName
                        id = $apiId
                    }
                }
            }
        }
    }
} | ConvertTo-Json -Depth 30

$logicAppUri = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Logic/workflows/$LogicAppName`?api-version=2019-05-01"
Invoke-AzRestMethod -Path $logicAppUri -Method PUT -Payload $logicAppBody | Out-Null
Write-Success "Logic App deployed"

# Get the trigger URL
Write-Step "Retrieving HTTP trigger URL..."
$callbackUri = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Logic/workflows/$LogicAppName/triggers/manual/listCallbackUrl?api-version=2019-05-01"
$callbackResponse = Invoke-AzRestMethod -Path $callbackUri -Method POST
$callbackContent = $callbackResponse.Content | ConvertFrom-Json
$triggerUrl = $callbackContent.value
if (-not $triggerUrl) { $triggerUrl = $callbackContent.properties.value }

# Check if trigger URL is already configured in the web app
$existingUrl = $null
try {
    $appSettings = (Invoke-AzRestMethod -Path "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$WebAppName/config/appsettings/list?api-version=2022-03-01" -Method POST).Content | ConvertFrom-Json
    $existingUrl = $appSettings.properties.'LogicApp__TriggerUrl'
} catch {}

$triggerUrlChanged = $existingUrl -ne $triggerUrl

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Deployment Complete!" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Success "Logic App: $LogicAppName"
Write-Success "Connection: $ConnectionName"

if ($needsAuth) {
    Write-Host ""
    Write-Info "ACTION REQUIRED: Authorize the ADO connection"
    Write-Host "  1. Go to: Azure Portal `u{2192} Logic App `u{2192} $LogicAppName `u{2192} Logic App Designer" -ForegroundColor White
    Write-Host "  2. Click on a red/warning ADO action `u{2192} Sign in with your Entra ID credentials" -ForegroundColor White
    Write-Host "  3. Save the Logic App in the designer" -ForegroundColor White
} else {
    Write-Success "ADO connection is authorized"
}

if ($triggerUrl) {
    Write-Host ""
    if ($triggerUrlChanged) {
        Write-Info "Trigger URL has changed. Update the App Service config:"
        Write-Host "  Set-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName -AppSettings @{LogicApp__TriggerUrl='$triggerUrl'; OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID='1f8ac8e7-1665-4a19-974e-f609ce3a3ac1'}" -ForegroundColor White
    } else {
        Write-Success "Trigger URL is already configured in $WebAppName"
    }
} else {
    Write-Info "Could not retrieve trigger URL. Get it from: Azure Portal `u{2192} Logic App `u{2192} $LogicAppName `u{2192} Overview"
}

Write-Host ""
