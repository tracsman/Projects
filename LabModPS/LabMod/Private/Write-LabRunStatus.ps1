function Write-LabRunStatus {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$RunId,

        [Parameter(Mandatory)]
        [ValidateSet('Queued', 'Running', 'Completed', 'Failed')]
        [string]$Status,

        [Parameter(Mandatory)]
        [string]$Message,

        [int]$TenantId,
        [string]$Action = 'LabVmRequest',
        [int]$ExitCode,
        [datetime]$StartedAt,
        [datetime]$CompletedAt,
        [string]$StatusPath
    )

    if (-not (Test-Path $script:LabLogDirectory)) {
        New-Item -ItemType Directory -Path $script:LabLogDirectory -Force | Out-Null
    }

    if (-not $PSBoundParameters.ContainsKey('StatusPath') -or [string]::IsNullOrWhiteSpace($StatusPath)) {
        $StatusPath = Join-Path $script:LabLogDirectory ("LabMod-Status-{0}.json" -f $RunId)
    }

    $payload = [ordered]@{
        runId = $RunId
        status = $Status
        message = $Message
        action = $Action
        computerName = $env:COMPUTERNAME
        updatedAt = (Get-Date).ToString('O')
        startedAt = if ($PSBoundParameters.ContainsKey('StartedAt')) { $StartedAt.ToString('O') } else { $null }
        completedAt = if ($PSBoundParameters.ContainsKey('CompletedAt')) { $CompletedAt.ToString('O') } else { $null }
        exitCode = if ($PSBoundParameters.ContainsKey('ExitCode')) { $ExitCode } else { $null }
    }

    if ($PSBoundParameters.ContainsKey('TenantId')) {
        $payload.tenantId = $TenantId
    }

    $payload | ConvertTo-Json -Compress -Depth 5 | Set-Content -Path $StatusPath -Encoding UTF8

    $logLevel = if ($Status -eq 'Failed') { 'Error' } else { 'Information' }
    Write-LabLogEntry -Message $Message -Level $logLevel -RunId $RunId -Action $Action -Data @{
        status = $Status
        statusPath = $StatusPath
        exitCode = $payload.exitCode
    }
}
