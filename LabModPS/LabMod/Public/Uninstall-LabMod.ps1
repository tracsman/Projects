function Uninstall-LabMod {
    $Destination = 'C:\Program Files\WindowsPowerShell\Modules\LabMod\'
    If (Test-Path $Destination) {
        Try {
            Remove-Item $Destination -Recurse
            Write-Host "LabMod PowerShell Module removed" -ForegroundColor Green
        }
        Catch {
            Write-Warning "The LabMod PowerShell Module was not removed."
            Write-Warning "You should manually delete the LabMod directory at:"
            Write-Warning $Destination
        } #End Try
    }
    Else {
        Write-Host "The LabMod PowerShell Module was not found on this machine."
    } # End If
    
    Remove-Module -Name LabMod -ErrorAction SilentlyContinue
    Write-Host "LabMod module unloaded from memory" -ForegroundColor Green

    Write-Host "LabMod removed" -ForegroundColor Green
} # End Function