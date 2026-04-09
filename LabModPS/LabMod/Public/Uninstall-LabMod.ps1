function Uninstall-LabMod {
    <#
    .SYNOPSIS
        Removes the LabMod module from the local machine.

    .DESCRIPTION
        Deletes the LabMod module directory from the PowerShell 7 modules path,
        unloads the module from the current session, and confirms removal.
        Requires an elevated session.

    .EXAMPLE
        Uninstall-LabMod

        Removes the LabMod module files and unloads it from memory.
    #>
    # Admin Session Check
    if (-not (Assert-LabAdminContext -WarnOnly)) {
        Return
    }

    #$Destination = 'C:\Program Files\WindowsPowerShell\Modules\LabMod\'
    $Destination = 'C:\Program Files\PowerShell\7\Modules\LabMod\'

    If (Test-Path $Destination) {
        Try {
            Remove-Item $Destination -Recurse
            Write-Log "LabMod PowerShell Module removed"
        }
        Catch {
            Write-Warning "The LabMod PowerShell Module was not removed."
            Write-Warning "You should manually delete the LabMod directory at:"
            Write-Warning $Destination
        } #End Try
    }
    Else {
        Write-Log "The LabMod PowerShell Module was not found on this machine."
    } # End If
    
    Remove-Module -Name LabMod -ErrorAction SilentlyContinue
    Write-Log "LabMod module unloaded from memory"

    Write-Log "LabMod removed"
} # End Function