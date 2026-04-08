function Start-LabVmRequest {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true, HelpMessage = 'Enter Tenant ID')]
        [ValidateRange(10, 99)]
        [int]$TenantID,

        [ValidateSet('Server2025', 'Ubuntu')]
        [string]$OS = 'Server2025',

        [Parameter(Mandatory = $false, HelpMessage = 'Optional server admin credential. If omitted, prompt interactively.')]
        [System.Management.Automation.PSCredential]$AdminCred,

        [Parameter(Mandatory = $false, HelpMessage = 'Optional environment user credential. If omitted, prompt interactively.')]
        [System.Management.Automation.PSCredential]$UserCred
    )

    $startedAt = Get-Date
    $lab = ($env:COMPUTERNAME.Split('-'))[0]
    $runId = 'labvm-{0}-cust{1}-{2}-{3}' -f $lab.ToLowerInvariant(), $TenantID, $startedAt.ToString('yyyyMMddHHmmss'), ([Guid]::NewGuid().ToString('N'))
    $statusPath = Join-Path $script:LabLogDirectory ("LabMod-Status-{0}.json" -f $runId)
    $action = 'Start-LabVmRequest'
    $vmPattern = '{0}-ER-{1}-VM*' -f $lab, $TenantID

    $result = [ordered]@{
        RunId = $runId
        TenantId = $TenantID
        Lab = $lab
        OS = $OS
        Action = $action
        Status = 'Preview'
        Success = $false
        StartedAt = $startedAt
        CompletedAt = $null
        StatusPath = $statusPath
        LogPath = $script:LabCommonLogPath
        CreatedVmNames = @()
        Message = 'Not started.'
    }

    if (-not $PSCmdlet.ShouldProcess("Tenant $TenantID on $env:COMPUTERNAME", "Create Lab VM ($OS)")) {
        $result.Message = 'Preview only. No VM was created.'
        [pscustomobject]$result
        return
    }

    Assert-LabAdminContext

    $existingVmNames = @(Get-VM -Name $vmPattern -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)

    Write-LabRunStatus -RunId $runId -TenantId $TenantID -Action $action -Status 'Queued' -Message "Lab VM request queued for tenant $TenantID ($OS)." -StartedAt $startedAt -StatusPath $statusPath
    Write-LabLogEntry -RunId $runId -TenantId $TenantID -Action $action -Message "Starting Lab VM request for tenant $TenantID ($OS)." -Data @{
        statusPath = $statusPath
        logPath = $script:LabCommonLogPath
    }

    try {
        Write-LabRunStatus -RunId $runId -TenantId $TenantID -Action $action -Status 'Running' -Message "Creating Lab VM for tenant $TenantID ($OS)." -StartedAt $startedAt -StatusPath $statusPath

        New-LabVM -TenantID $TenantID -OS $OS -AdminCred $AdminCred -UserCred $UserCred

        $currentVmNames = @(Get-VM -Name $vmPattern -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
        $createdVmNames = @($currentVmNames | Where-Object { $_ -notin $existingVmNames })

        if ($createdVmNames.Count -eq 0) {
            throw "New-LabVM completed without a newly detected VM for tenant $TenantID."
        }

        $completedAt = Get-Date
        $message = "Created VM(s): {0}" -f ($createdVmNames -join ', ')

        Write-LabRunStatus -RunId $runId -TenantId $TenantID -Action $action -Status 'Completed' -Message $message -ExitCode 0 -StartedAt $startedAt -CompletedAt $completedAt -StatusPath $statusPath

        $result.Status = 'Completed'
        $result.Success = $true
        $result.CompletedAt = $completedAt
        $result.CreatedVmNames = $createdVmNames
        $result.Message = $message
        [pscustomobject]$result
    }
    catch {
        $completedAt = Get-Date
        $message = $_.Exception.Message

        Write-LabRunStatus -RunId $runId -TenantId $TenantID -Action $action -Status 'Failed' -Message $message -ExitCode 1 -StartedAt $startedAt -CompletedAt $completedAt -StatusPath $statusPath
        Write-LabLogEntry -RunId $runId -TenantId $TenantID -Action $action -Level 'Error' -Message "Lab VM request failed for tenant $TenantID ($OS): $message"

        throw
    }
}
