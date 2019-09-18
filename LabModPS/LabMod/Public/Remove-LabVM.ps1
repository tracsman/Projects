Function Remove-LabVM {
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
    If (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Warning "This script must be run elevated as Administrator!"
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
    $Mins = $TimeDiff.Minutes
    $Secs = $TimeDiff.Seconds
    $RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Delete completed successfully" -ForegroundColor Green
    Write-Host "Time to delete $VMCount VM(s): $RunTime"
    Write-Host
}
