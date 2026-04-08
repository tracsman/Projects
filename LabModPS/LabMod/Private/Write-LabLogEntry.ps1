function Write-LabLogEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$Message,

        [ValidateSet('Verbose', 'Information', 'Warning', 'Error')]
        [string]$Level = 'Information',

        [string]$RunId,
        [int]$TenantId,
        [string]$Action,
        [hashtable]$Data
    )

    if (-not (Test-Path $script:LabLogDirectory)) {
        New-Item -ItemType Directory -Path $script:LabLogDirectory -Force | Out-Null
    }

    $entry = [ordered]@{
        timestamp = (Get-Date).ToString('O')
        level = $Level
        computerName = $env:COMPUTERNAME
        userName = [Security.Principal.WindowsIdentity]::GetCurrent().Name
        message = $Message
    }

    if ($PSBoundParameters.ContainsKey('RunId') -and -not [string]::IsNullOrWhiteSpace($RunId)) {
        $entry.runId = $RunId
    }

    if ($PSBoundParameters.ContainsKey('TenantId')) {
        $entry.tenantId = $TenantId
    }

    if ($PSBoundParameters.ContainsKey('Action') -and -not [string]::IsNullOrWhiteSpace($Action)) {
        $entry.action = $Action
    }

    if ($PSBoundParameters.ContainsKey('Data') -and $null -ne $Data) {
        $entry.data = $Data
    }

    $entry | ConvertTo-Json -Compress -Depth 5 | Add-Content -Path $script:LabCommonLogPath -Encoding UTF8
}
