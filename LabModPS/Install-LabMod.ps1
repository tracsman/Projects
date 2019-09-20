# Install-LabMod Module
# To kick me off from a URL run the following:
# (new-object Net.WebClient).DownloadString("https://raw.githubusercontent.com/tracsman/Projects/master/LabModPS/Install-LabMod.ps1") | Invoke-Expression

function Install-LabMod {

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
    
    $Destination = 'C:\Program Files\WindowsPowerShell\Modules\LabMod\'
    New-Item -Path ($Destination) -ItemType Directory -Force | Out-Null
    New-Item -Path ($Destination + "Public") -ItemType Directory -Force | Out-Null

    Write-Host

    ForEach ($File in $FileName) {
        $webClient = new-object System.Net.WebClient
        $webClient.DownloadFile( $uri + $File, $Destination + $File )
        Write-Host "Copied successfully:" $File 
    }

    $executionPolicy = (Get-ExecutionPolicy)
    $executionRestricted = ($executionPolicy -eq "Restricted")
    If ($executionRestricted) {
        Write-Warning "Your execution policy is $executionPolicy, this means you will not be able import or use any scripts including modules."
        Write-Warning "To fix this change your execution policy to something like RemoteSigned."
        Write-Host
        Write-Warning "     PS> Set-ExecutionPolicy RemoteSigned"
        Write-Host
        Write-Warning "For more information execute:"
        Write-Host
        Write-Warning "     PS> Get-Help about_execution_policies"
    }

    Write-Host "LabMod is installed and ready to use" -Foreground Green
    Write-Host
} # End Function

Install-LabMod
