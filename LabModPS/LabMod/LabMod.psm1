# region: module-scope initialisation
$script:IsInteractive = [Environment]::UserInteractive
$script:LabLogDirectory = 'C:\Hyper-V\Logs'
$script:LabCommonLogPath = Join-Path $script:LabLogDirectory 'LabMod.log.jsonl'
$script:SubscriptionMap = @{
    'ExpressRoute-Lab'     = '4bffbb15-d414-4874-a2e4-c548c6d45e2a'
    'ExpressRoute-lab-bvt' = '79573dd5-f6ea-4fdc-a3aa-d05586980843'
    'Hybrid-PM-Demo-1'     = '28bf59a7-de1b-4c94-92ec-a5aab87885f7'
    'Hybrid-PM-Test-1'     = 'f2a54638-fcdc-443b-a6fe-5ea64d2c9e0e'
    'Hybrid-PM-Test-2'     = '43467485-b19a-4b68-ac94-c9a8e980ca7f'
    'Hybrid-PM-Repro-1'    = '79573dd5-f6ea-4fdc-a3aa-d05586980843'
}
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
