function Update-LabMod {
    [CmdletBinding()]
    param (
        [switch]$Force=$false)

    # Admin Session Check
    If (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warning "This script must be run elevated as Administrator!"
        Return
    }
    
    If (!$Force) {
        # Get Online Version
        [net.httpwebrequest]$httpwebrequest = [net.webrequest]::create('https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/LabMod/LabMod.psd1')
        [net.httpWebResponse]$httpwebresponse = $httpwebrequest.getResponse()
        $reader = new-object IO.StreamReader($httpwebresponse.getResponseStream())
        $content = $reader.ReadToEnd()
        $reader.Close()
        $i=355
        $OnlineVersion = ""
        Do {
            $OnlineVersion = $OnlineVersion + $content[$i]
            $i=$i+1
        }
        Until ($content[$i+2] -eq "`n")

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
        $FileName += 'Public/New-LabECX.ps1'
        $FileName += 'Public/New-LabVM.ps1'
        $FileName += 'Public/New-LabVMDrive.ps1'
        $FileName += 'Public/Remove-LabECX.ps1'
        $FileName += 'Public/Remove-LabVM.ps1'
        $FileName += 'Public/Uninstall-LabMod.ps1'
        $FileName += 'Public/Update-LabMod.ps1'
        $FileName += 'Public/Update-LabLibrary.ps1'
        $FileName += 'Public/Build-LabBaseVHDX.ps1'
        $FileName += 'Public/Build-LabBaseVM.ps1'

        $Destination = 'C:\Program Files\WindowsPowerShell\Modules\LabMod\'

        Write-Host

        ForEach ($File in $FileName) {
            $webClient = new-object System.Net.WebClient
            $webClient.DownloadFile( $uri + $File, $Destination + $File )
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
