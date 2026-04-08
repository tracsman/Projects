function Update-LabMod {
    [CmdletBinding()]
    param (
        [switch]$Force=$false)

    # Admin Session Check
    if (-not (Assert-LabAdminContext -WarnOnly)) {
        Return
    }
    
    If (!$Force) {
        # Get Online Version
        [net.httpwebrequest]$httpwebrequest = [net.webrequest]::create('https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/LabMod/LabMod.psd1')
        [net.httpWebResponse]$httpwebresponse = $httpwebrequest.getResponse()
        $reader = new-object IO.StreamReader($httpwebresponse.getResponseStream())
        $content = $reader.ReadToEnd()
        $reader.Close()
        $versionMatch = [regex]::Match($content, "ModuleVersion\s*=\s*'(?<version>[^']+)'")
        if (-not $versionMatch.Success) {
            throw "Unable to determine the online LabMod version from the module manifest."
        }
        $OnlineVersion = $versionMatch.Groups['version'].Value

        # Get Installed Version
        Try {$CurrentVersion = (Get-Module LabMod -ListAvailable).Version.ToString()}
        Catch {$CurrentVersion = "0.0.0.0"}
    }

    # Version Compare
    If ($OnlineVersion -ne $CurrentVersion -or $Force) {

        $uri = 'https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/LabMod/'

        $FileName = @()
        $FileName += 'LabMod.psd1'
        $FileName += 'LabMod.psm1'
        $FileName += 'Private/Assert-LabAdminContext.ps1'
        $FileName += 'Private/Write-LabLogEntry.ps1'
        $FileName += 'Private/Write-LabRunStatus.ps1'
        $FileName += 'Public/Copy-ToUbuntu.ps1'
        $FileName += 'Public/Get-LabECX.ps1'
        $FileName += 'Public/New-LabECX.ps1'
        $FileName += 'Public/Start-LabVmRequest.ps1'
        $FileName += 'Public/New-LabVM.ps1'
        $FileName += 'Public/New-LabVMDrive.ps1'
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
            Write-Host "Copied successfully:" $File 
        }

        Write-Host "LabMod is updated, please reopen any active sessions to use" -Foreground Green
        Write-Host
    }
    Else {
        Write-Host "LabMod is current, no updates needed" -Foreground Green
        Write-Host
    }
} # End Function
