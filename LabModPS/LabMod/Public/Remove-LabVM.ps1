Function Remove-LabVM {
    <#
    .SYNOPSIS
        Removes one or more lab VMs and their associated VHDX files for a given tenant.

    .DESCRIPTION
        Stops, removes the Hyper-V VM object, and deletes the VHDX disk for the specified tenant.
        By default all VMs for the tenant are removed. Use -VMInstance to target a specific VM
        number (e.g. 01, 02).

    .PARAMETER TenantID
        The tenant ID whose VMs should be removed.

    .PARAMETER VMInstance
        The specific VM instance number to remove (e.g. 1 for VM01, 2 for VM02).
        When 0 (the default), all VMs for the tenant are removed.

    .EXAMPLE
        Remove-LabVM -TenantID 16

        Removes all VMs for tenant 16 on this host.

    .EXAMPLE
        Remove-LabVM -TenantID 16 -VMInstance 2

        Removes only VM02 for tenant 16.

    .LINK
        New-LabVM
    #>

    [CmdletBinding()]
    param (
        [Parameter(Mandatory = $true, ValueFromPipeline = $true, HelpMessage = 'Enter Tenant ID')]
        [int]$TenantID,
        [int]$VMInstance=0)

    # 1. Initialize
    $StartTime = Get-Date

    # Lab Environment Variables
    $Lab = ($env:COMPUTERNAME.Split("-"))[0]
    If ($VMInstance -eq 0) {$VMSearch = "*"}
    Else {$VMSearch = $VMInstance.ToString("00") + "*"}
    $VMName = $Lab + "-ER-" + $TenantID + "-VM" + $VMSearch
    $VHDDest = "C:\Hyper-V\Virtual Hard Disks\"
    $VMDisk = $VHDDest + $VMName + ".vhdx"  
    $VMCount = (Get-VM -Name $VMName).Count

    # 2. Validate
    # Admin Session Check
    if (-not (Assert-LabAdminContext -WarnOnly)) {
        Return
    }

    # 3. Stop VM
    Stop-VM -Name $VMName

    # 4. Delete VM
    Remove-VM -Name $VMName -Force

    # 5. Delete VHDX
    Remove-Item -Path $VMDisk

    # 6. End nicely
    $EndTime = Get-Date
    $TimeDiff = New-TimeSpan $StartTime $EndTime
    $RunTime = $TimeDiff.ToString('hh\:mm\:ss')
    Write-Log "Delete completed successfully" -TimeStamp
    Write-Log "Time to delete $VMCount VM(s): $RunTime"
    Write-Host
}
