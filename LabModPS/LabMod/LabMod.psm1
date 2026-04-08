# region: module-scope initialisation
$script:IsInteractive = [Environment]::UserInteractive
$script:LabLogDirectory = 'C:\Hyper-V\Logs'
$script:LabCommonLogPath = Join-Path $script:LabLogDirectory 'LabMod.log.jsonl'
# endregion

# region: internal helper(s)
function Write-Log {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [switch]$TimeStamp = $false
    )

    Write-LabLogEntry -Message $Message -Level 'Information'

    if ($script:IsInteractive) {
        # Friendly local UX
        if ($TimeStamp) { Write-Host (Get-Date)' - ' -NoNewline }
        if ($TimeStamp) { Write-Host $Message -ForegroundColor Cyan } else { Write-Host $Message }
    }
    else {
        # Runbook-safe logging
        if ($TimeStamp) {Write-Verbose "$(Get-Date) - $Message" } else { Write-Verbose $Message }
    }
}
# endregion


# Get private and public function definition files.
    $Private = @( Get-ChildItem -Path $PSScriptRoot\Private\*.ps1 -ErrorAction SilentlyContinue )

    $Public  = @( Get-ChildItem -Path $PSScriptRoot\Public\*.ps1 -ErrorAction SilentlyContinue )

#Dot source the files
    Foreach($import in @($Private + $Public))
    {
        Try
        {
            . $import.fullname
        }
        Catch
        {
            Write-Error -Message "Failed to import function $($import.fullname): $_"
        }
    }

$ManifestData = Import-PowerShellDataFile -Path $PSScriptRoot\LabMod.psd1
$script:XMLSchemaVersion = "$($ManifestData.ModuleVersion.Split('.')[0]).$($ManifestData.ModuleVersion.Split('.')[1])"
