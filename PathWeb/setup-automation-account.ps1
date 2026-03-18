param(
    [string]$SubscriptionId = "4bffbb15-d414-4874-a2e4-c548c6d45e2a",
    [string]$ResourceGroupName = "LabInfrastructure",
    [string]$Location = "West US 2",
    [string]$AutomationAccountName = "PathWeb-Automation",
    [string]$RoleName = "Contributor",
    [int]$PrincipalRetryCount = 18,
    [int]$PrincipalRetryDelaySeconds = 10,
    [int]$RoleRetryCount = 18,
    [int]$RoleRetryDelaySeconds = 10
)

$ErrorActionPreference = "Stop"

function Write-Step {
    param([string]$Message)
    Write-Host "`n==> $Message" -ForegroundColor Cyan
}

function Require-AzCli {
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        throw "Azure CLI ('az') is not installed or not in PATH."
    }
}

function Require-Parameter {
    param(
        [string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.StartsWith("<")) {
        throw "Parameter '$Name' is not set. Update the placeholder value before running the script."
    }
}

function Invoke-AzCli {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $cliArgs = $Arguments + @('--only-show-errors')
    $output = & az @cliArgs 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).Trim()

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "Azure CLI command failed: az $($cliArgs -join ' ')`n$text"
    }

    return [PSCustomObject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Get-AutomationAccount {
    $result = Invoke-AzCli -Arguments @(
        'automation', 'account', 'show',
        '--name', $AutomationAccountName,
        '--resource-group', $ResourceGroupName,
        '--output', 'json'
    ) -AllowFailure

    if ($result.ExitCode -ne 0) {
        return $null
    }

    return $result.Output | ConvertFrom-Json
}

function Ensure-SystemAssignedIdentity {
    Write-Step "Enabling system-assigned managed identity"

    $result = Invoke-AzCli -Arguments @(
        'automation', 'account', 'update',
        '--name', $AutomationAccountName,
        '--resource-group', $ResourceGroupName,
        '--assign-identity',
        '--output', 'table'
    ) -AllowFailure

    if ($result.ExitCode -eq 0) {
        return
    }

    if ($result.Output -notmatch 'unrecognized\s+arguments:\s+--assign-identity') {
        throw "Failed to enable managed identity on '$AutomationAccountName'.`n$($result.Output)"
    }

    Write-Host "Current Azure CLI automation extension does not support '--assign-identity'. Falling back to generic resource update." -ForegroundColor Yellow

    $automationAccountIdResult = Invoke-AzCli -Arguments @(
        'automation', 'account', 'show',
        '--name', $AutomationAccountName,
        '--resource-group', $ResourceGroupName,
        '--query', 'id',
        '--output', 'tsv'
    )

    Invoke-AzCli -Arguments @(
        'resource', 'update',
        '--ids', $automationAccountIdResult.Output,
        '--set', 'identity.type=SystemAssigned',
        '--output', 'table'
    ) | Out-Null
}

function Wait-ForPrincipalId {
    for ($attempt = 1; $attempt -le $PrincipalRetryCount; $attempt++) {
        $principalId = Invoke-AzCli -Arguments @(
            'automation', 'account', 'show',
            '--name', $AutomationAccountName,
            '--resource-group', $ResourceGroupName,
            '--query', 'identity.principalId',
            '--output', 'tsv'
        )

        if (-not [string]::IsNullOrWhiteSpace($principalId.Output)) {
            return $principalId.Output
        }

        if ($attempt -lt $PrincipalRetryCount) {
            Write-Host "Managed identity principal not ready yet. Waiting $PrincipalRetryDelaySeconds second(s)... ($attempt/$PrincipalRetryCount)" -ForegroundColor Yellow
            Start-Sleep -Seconds $PrincipalRetryDelaySeconds
        }
    }

    throw "Managed identity principal ID did not become available for '$AutomationAccountName' after $PrincipalRetryCount attempts."
}

function Test-RoleAssignmentExists {
    param(
        [Parameter(Mandatory)]
        [string]$PrincipalId
    )

    $scope = "/subscriptions/$SubscriptionId"
    $result = Invoke-AzCli -Arguments @(
        'role', 'assignment', 'list',
        '--assignee-object-id', $PrincipalId,
        '--scope', $scope,
        '--query', "[?roleDefinitionName=='$RoleName'] | [0].id",
        '--output', 'tsv'
    ) -AllowFailure

    return ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output))
}

function Get-RoleAssignmentId {
    param(
        [Parameter(Mandatory)]
        [string]$PrincipalId
    )

    $scope = "/subscriptions/$SubscriptionId"
    $result = Invoke-AzCli -Arguments @(
        'role', 'assignment', 'list',
        '--assignee-object-id', $PrincipalId,
        '--scope', $scope,
        '--query', "[?roleDefinitionName=='$RoleName'] | [0].id",
        '--output', 'tsv'
    ) -AllowFailure

    if ($result.ExitCode -ne 0) {
        return $null
    }

    return $result.Output
}

function Ensure-RoleAssignment {
    param(
        [Parameter(Mandatory)]
        [string]$PrincipalId
    )

    if (Test-RoleAssignmentExists -PrincipalId $PrincipalId) {
        Write-Host "Role '$RoleName' is already assigned at subscription scope. Skipping." -ForegroundColor Green
        return
    }

    $scope = "/subscriptions/$SubscriptionId"
    for ($attempt = 1; $attempt -le $RoleRetryCount; $attempt++) {
        $result = Invoke-AzCli -Arguments @(
            'role', 'assignment', 'create',
            '--role', $RoleName,
            '--scope', $scope,
            '--assignee-object-id', $PrincipalId,
            '--output', 'table'
        ) -AllowFailure

        if ($result.ExitCode -eq 0) {
            return
        }

        if (Test-RoleAssignmentExists -PrincipalId $PrincipalId) {
            Write-Host "Role '$RoleName' already exists after retry check. Skipping." -ForegroundColor Green
            return
        }

        if ($attempt -lt $RoleRetryCount) {
            Write-Host "RBAC assignment not ready yet. Waiting $RoleRetryDelaySeconds second(s)... ($attempt/$RoleRetryCount)" -ForegroundColor Yellow
            Start-Sleep -Seconds $RoleRetryDelaySeconds
        }
        else {
            throw "Failed to assign role '$RoleName' to Automation Account identity after $RoleRetryCount attempts.`n$($result.Output)"
        }
    }
}

Require-AzCli
Require-Parameter -Name "SubscriptionId" -Value $SubscriptionId
Require-Parameter -Name "ResourceGroupName" -Value $ResourceGroupName
Require-Parameter -Name "Location" -Value $Location
Require-Parameter -Name "AutomationAccountName" -Value $AutomationAccountName

Write-Step "Checking Azure login"
$loginCheck = Invoke-AzCli -Arguments @('account', 'show') -AllowFailure
if ($loginCheck.ExitCode -ne 0) {
    throw "Not logged in to Azure CLI. Run 'az login' first."
}

Write-Step "Setting subscription"
Invoke-AzCli -Arguments @('account', 'set', '--subscription', $SubscriptionId) | Out-Null

Write-Step "Ensuring resource group exists"
Invoke-AzCli -Arguments @('group', 'create', '--name', $ResourceGroupName, '--location', $Location, '--output', 'table') | Out-Null

Write-Step "Ensuring Automation Account exists"
$automationAccount = Get-AutomationAccount
if ($null -eq $automationAccount) {
    Invoke-AzCli -Arguments @(
        'automation', 'account', 'create',
        '--name', $AutomationAccountName,
        '--resource-group', $ResourceGroupName,
        '--location', $Location,
        '--output', 'table'
    ) | Out-Null
}
else {
    Write-Host "Automation Account '$AutomationAccountName' already exists. Skipping create." -ForegroundColor Green
}

Ensure-SystemAssignedIdentity

Write-Step "Waiting for Automation Account principal ID"
$PrincipalId = Wait-ForPrincipalId

Write-Step "Ensuring role '$RoleName' at subscription scope"
Ensure-RoleAssignment -PrincipalId $PrincipalId

Write-Step "Gathering final verification details"
$AutomationAccountId = Invoke-AzCli -Arguments @(
    'automation', 'account', 'show',
    '--name', $AutomationAccountName,
    '--resource-group', $ResourceGroupName,
    '--query', 'id',
    '--output', 'tsv'
)

$RoleAssignmentId = Get-RoleAssignmentId -PrincipalId $PrincipalId
$RoleAssignmentStatus = if ([string]::IsNullOrWhiteSpace($RoleAssignmentId)) { 'NotFound' } else { 'Present' }

Write-Step "Completed"
Write-Host "Automation Account Name : $AutomationAccountName" -ForegroundColor Green
Write-Host "Automation Account ID   : $($AutomationAccountId.Output)" -ForegroundColor Green
Write-Host "Resource Group          : $ResourceGroupName" -ForegroundColor Green
Write-Host "Subscription ID         : $SubscriptionId" -ForegroundColor Green
Write-Host "Principal ID            : $PrincipalId" -ForegroundColor Green
Write-Host "Role Assigned           : $RoleName" -ForegroundColor Green
Write-Host "Role Assignment Status  : $RoleAssignmentStatus" -ForegroundColor Green
Write-Host "Role Assignment ID      : $RoleAssignmentId" -ForegroundColor Green
Write-Host "`nNext: add these values to PathWeb config so the app can submit jobs." -ForegroundColor Yellow
