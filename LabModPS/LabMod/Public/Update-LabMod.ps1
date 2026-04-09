function Update-LabMod {
    <#
    .SYNOPSIS
        Updates the LabMod module to the latest version from GitHub.

    .DESCRIPTION
        Compares the installed LabMod version against the version published on GitHub.
        If a newer version is available (or -Force is specified), downloads all module
        files to the local PowerShell 7 modules directory. Requires an elevated session.

    .PARAMETER Force
        Skips the version comparison and re-downloads all files regardless of the
        currently installed version.

    .EXAMPLE
        Update-LabMod

        Checks for a newer version and updates if available.

    .EXAMPLE
        Update-LabMod -Force

        Re-downloads all module files regardless of the current version.
    #>

    [CmdletBinding()]
    param (
        [switch]$Force=$false)

    # Admin Session Check
    $assertLabAdminContext = Get-Command Assert-LabAdminContext -ErrorAction SilentlyContinue
    if ($null -ne $assertLabAdminContext) {
        $isAdmin = Assert-LabAdminContext -WarnOnly
    }
    else {
        $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)

        if (-not $isAdmin) {
            Write-Warning "This command must be run elevated as Administrator!"
        }
    }

    if (-not $isAdmin) {
        Return
    }
    
    If (!$Force) {
        # Get Online Version
        [net.httpwebrequest]$httpwebrequest = [net.webrequest]::create('https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/LabMod/LabMod.psd1')
        $httpwebresponse = $null
        $reader = $null
        try {
            [net.httpWebResponse]$httpwebresponse = $httpwebrequest.getResponse()
            $reader = new-object IO.StreamReader($httpwebresponse.getResponseStream())
            $content = $reader.ReadToEnd()
        }
        finally {
            if ($null -ne $reader) { $reader.Close() }
            if ($null -ne $httpwebresponse) { $httpwebresponse.Close() }
        }
        $versionMatch = [regex]::Match($content, "ModuleVersion\s*=\s*'(?<version>[^']+)'")
        if (-not $versionMatch.Success) {
            throw "Unable to determine the online LabMod version from the module manifest."
        }
        [version]$OnlineVersion = $versionMatch.Groups['version'].Value

        # Get Installed Version
        Try {[version]$CurrentVersion = (Get-Module LabMod -ListAvailable).Version}
        Catch {[version]$CurrentVersion = "0.0.0.0"}
    }

    # Version Compare
    If ($OnlineVersion -gt $CurrentVersion -or $Force) {

        $uri = 'https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/LabMod/'

        $FileName = @()
        $FileName += 'LabMod.psd1'
        $FileName += 'LabMod.psm1'
        $FileName += 'Private/Assert-LabAdminContext.ps1'
        $FileName += 'Private/Write-LabLogEntry.ps1'
        $FileName += 'Private/Write-LabRunStatus.ps1'
        $FileName += 'Private/New-LabVMDrive.ps1'
        $FileName += 'Public/Copy-ToUbuntu.ps1'
        $FileName += 'Public/Get-LabECX.ps1'
        $FileName += 'Public/New-LabECX.ps1'
        $FileName += 'Public/Start-LabVmRequest.ps1'
        $FileName += 'Public/New-LabVM.ps1'
        $FileName += 'Public/Remove-LabECX.ps1'
        $FileName += 'Public/Remove-LabVM.ps1'
        $FileName += 'Public/Uninstall-LabMod.ps1'
        $FileName += 'Public/Update-LabMod.ps1'
        $FileName += 'Public/Update-LabLibrary.ps1'
        $FileName += 'Public/Build-LabBaseVHDX.ps1'
        $FileName += 'Public/Build-LabBaseVM.ps1'

        $Destination = 'C:\Program Files\PowerShell\7\Modules\LabMod\'

        Write-Host

        ForEach ($File in $FileName) {
            $targetPath = Join-Path $Destination $File
            $targetDirectory = Split-Path -Path $targetPath -Parent
            if (-not (Test-Path $targetDirectory)) {
                New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            }
            $webClient = new-object System.Net.WebClient
            $webClient.DownloadFile( $uri + $File, $targetPath )
            Write-Log "Copied successfully: $File"
        }

        Write-Log "LabMod is updated, please reopen any active sessions to use"
        Write-Host
    }
    Else {
        Write-Log "LabMod is current, no updates needed"
        Write-Host
    }
} # End Function
