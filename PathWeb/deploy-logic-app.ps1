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
    [string]$AdoLogicAppName = "PathWeb-ADO",
    [string]$AdoConnectionName = "PathWeb-ADO-conn",
    [string]$EmailLogicAppName = "PathWeb-Email",
    [string]$EmailConnectionName = "PathWeb-Email-conn",
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

function Get-DefinitionFile {
    param([string]$FileName)

    $file = Join-Path $PSScriptRoot "LogicApp" $FileName
    if (-not (Test-Path $file)) {
        throw "Definition file not found: $file"
    }

    Write-Success "Definition file found: $file"
    return $file
}

function Ensure-ApiConnection {
    param(
        [string]$Name,
        [string]$ManagedApiName,
        [string]$DisplayName
    )

    Write-Step "Checking API connection '$Name' ($ManagedApiName)..."
    $connectionUri = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/connections/$Name`?api-version=2016-06-01"

    try {
        $connResponse = Invoke-AzRestMethod -Path $connectionUri -Method GET
    } catch {
        $connResponse = $null
    }

    $needsAuth = $false
    if ($connResponse -and $connResponse.StatusCode -eq 200) {
        $connStatus = ($connResponse.Content | ConvertFrom-Json).properties.statuses[0].status
        if ($connStatus -eq "Connected") {
            Write-Success "API connection '$Name' exists and is authorized"
        } else {
            Write-Info "API connection '$Name' exists but status is: $connStatus (needs authorization)"
            $needsAuth = $true
        }
    } else {
        Write-Step "Creating API connection '$Name'..."
        $apiId = "/subscriptions/$subscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/$ManagedApiName"
        $connBody = @{
            location = $Location
            properties = @{
                api = @{ id = $apiId }
                displayName = $DisplayName
            }
        } | ConvertTo-Json -Depth 10

        Invoke-AzRestMethod -Path $connectionUri -Method PUT -Payload $connBody | Out-Null
        Write-Success "API connection '$Name' created"
        $needsAuth = $true
    }

    return @{
        Name = $Name
        ManagedApiName = $ManagedApiName
        ConnectionId = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/connections/$Name"
        ApiId = "/subscriptions/$subscriptionId/providers/Microsoft.Web/locations/$Location/managedApis/$ManagedApiName"
        NeedsAuth = $needsAuth
    }
}

function Deploy-LogicAppWorkflow {
    param(
        [string]$WorkflowName,
        [string]$DefinitionFile,
        [hashtable]$Connections
    )

    Write-Step "Deploying Logic App '$WorkflowName' from definition file..."
    $definition = Get-Content $DefinitionFile -Raw | ConvertFrom-Json -AsHashtable

    $connectionsValue = @{}
    foreach ($alias in $Connections.Keys) {
        $connection = $Connections[$alias]
        $connectionsValue[$alias] = @{
            connectionId = $connection.ConnectionId
            connectionName = $connection.Name
            id = $connection.ApiId
        }
    }

    $logicAppBody = @{
        location = $Location
        properties = @{
            state = "Enabled"
            definition = $definition
            parameters = @{
                "`$connections" = @{
                    value = $connectionsValue
                }
            }
        }
    } | ConvertTo-Json -Depth 30

    $logicAppUri = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Logic/workflows/$WorkflowName`?api-version=2019-05-01"
    Invoke-AzRestMethod -Path $logicAppUri -Method PUT -Payload $logicAppBody | Out-Null
    Write-Success "Logic App '$WorkflowName' deployed"
}

function Get-LogicAppTriggerUrl {
    param([string]$WorkflowName)

    Write-Step "Retrieving HTTP trigger URL for '$WorkflowName'..."
    $callbackUri = "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Logic/workflows/$WorkflowName/triggers/manual/listCallbackUrl?api-version=2019-05-01"
    $callbackResponse = Invoke-AzRestMethod -Path $callbackUri -Method POST
    $callbackContent = $callbackResponse.Content | ConvertFrom-Json
    $triggerUrl = $callbackContent.value
    if (-not $triggerUrl) {
        $triggerUrl = $callbackContent.properties.value
    }

    return $triggerUrl
}

function Get-WebAppSetting {
    param([string]$SettingName)

    try {
        $appSettings = (Invoke-AzRestMethod -Path "/subscriptions/$subscriptionId/resourceGroups/$ResourceGroup/providers/Microsoft.Web/sites/$WebAppName/config/appsettings/list?api-version=2022-03-01" -Method POST).Content | ConvertFrom-Json
        return $appSettings.properties.$SettingName
    } catch {
        return $null
    }
}

$adoDefinitionFile = Get-DefinitionFile "PathWeb-ADO.definition.json"
$emailDefinitionFile = Get-DefinitionFile "PathWeb-Email.definition.json"

$adoConnection = Ensure-ApiConnection -Name $AdoConnectionName -ManagedApiName "visualstudioteamservices" -DisplayName "Azure DevOps (PathWeb)"
$emailConnection = Ensure-ApiConnection -Name $EmailConnectionName -ManagedApiName "office365" -DisplayName "PathWeb Email"

Deploy-LogicAppWorkflow -WorkflowName $AdoLogicAppName -DefinitionFile $adoDefinitionFile -Connections @{
    visualstudioteamservices = $adoConnection
    "visualstudioteamservices-1" = $adoConnection
}

Deploy-LogicAppWorkflow -WorkflowName $EmailLogicAppName -DefinitionFile $emailDefinitionFile -Connections @{
    office365 = $emailConnection
}

$adoTriggerUrl = Get-LogicAppTriggerUrl -WorkflowName $AdoLogicAppName
$emailTriggerUrl = Get-LogicAppTriggerUrl -WorkflowName $EmailLogicAppName

$existingAdoUrl = Get-WebAppSetting -SettingName 'LogicApp__TriggerUrl'
$existingEmailUrl = Get-WebAppSetting -SettingName 'LogicApp__EmailTriggerUrl'

$adoTriggerUrlChanged = $existingAdoUrl -ne $adoTriggerUrl
$emailTriggerUrlChanged = $existingEmailUrl -ne $emailTriggerUrl

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Deployment Complete!" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Success "ADO Logic App: $AdoLogicAppName"
Write-Success "ADO Connection: $AdoConnectionName"
Write-Success "Email Logic App: $EmailLogicAppName"
Write-Success "Email Connection: $EmailConnectionName"

if ($adoConnection.NeedsAuth) {
    Write-Host ""
    Write-Info "ACTION REQUIRED: Authorize the ADO connection"
    Write-Host "  1. Go to: Azure Portal `u{2192} Logic App `u{2192} $AdoLogicAppName `u{2192} Logic App Designer" -ForegroundColor White
    Write-Host "  2. Click on a red/warning ADO action `u{2192} Sign in with your Entra ID credentials" -ForegroundColor White
    Write-Host "  3. Save the Logic App in the designer" -ForegroundColor White
} else {
    Write-Success "ADO connection is authorized"
}

if ($emailConnection.NeedsAuth) {
    Write-Host ""
    Write-Info "ACTION REQUIRED: Authorize the email connection"
    Write-Host "  1. Go to: Azure Portal `u{2192} Logic App `u{2192} $EmailLogicAppName `u{2192} Logic App Designer" -ForegroundColor White
    Write-Host "  2. Click the Send email action `u{2192} Sign in with the dedicated mailbox account" -ForegroundColor White
    Write-Host "  3. Save the Logic App in the designer" -ForegroundColor White
} else {
    Write-Success "Email connection is authorized"
}

if ($adoTriggerUrl) {
    Write-Host ""
    if ($adoTriggerUrlChanged) {
        Write-Info "Trigger URL has changed. Update the App Service config:"
        Write-Host "  Set-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName -AppSettings @{LogicApp__TriggerUrl='$adoTriggerUrl'; OVERRIDE_USE_MI_FIC_ASSERTION_CLIENTID='1f8ac8e7-1665-4a19-974e-f609ce3a3ac1'}" -ForegroundColor White
    } else {
        Write-Success "Trigger URL is already configured in $WebAppName"
    }
} else {
    Write-Info "Could not retrieve ADO trigger URL. Get it from: Azure Portal `u{2192} Logic App `u{2192} $AdoLogicAppName `u{2192} Overview"
}

if ($emailTriggerUrl) {
    Write-Host ""
    if ($emailTriggerUrlChanged) {
        Write-Info "Email trigger URL is ready for later app integration:"
        Write-Host "  Set-AzWebApp -ResourceGroupName $ResourceGroup -Name $WebAppName -AppSettings @{LogicApp__EmailTriggerUrl='$emailTriggerUrl'}" -ForegroundColor White
    } else {
        Write-Success "Email trigger URL is already configured in $WebAppName"
    }

    Write-Host ""
    Write-Info "Test the email Logic App with:"
    Write-Host "  .\test-email-logic-app.ps1 -TriggerUrl '$emailTriggerUrl' -To 'you@example.com'" -ForegroundColor White
} else {
    Write-Info "Could not retrieve email trigger URL. Get it from: Azure Portal `u{2192} Logic App `u{2192} $EmailLogicAppName `u{2192} Overview"
}

Write-Host ""
