function Build-LabBaseVHDX{
    [CmdletBinding()]
    param (
        [Parameter(ValueFromPipeline = $true, HelpMessage = 'Enter OS')]
        [ValidateSet("Server2019", "CentOS", "Ubuntu")]
        [string]$OS="Server2019")

    switch ($OS) {
        "Server2019" {
            $VMName = "Base2019"
            $ISO = "C:\Hyper-V\ISO\Microsoft\17763.379.190312-0539.rs5_release_svc_refresh_SERVER_EVAL_x64FRE_en-us.iso"
        }
        "CentOS" {
            $VMName = "BaseCentOS"
            $ISO = "C:\Hyper-V\ISO\Linux\CentOS-8-x86_64-1905-dvd1.iso"
        }
        "Ubuntu" {
            $VMName = "BaseUbuntu"
            $ISO = "C:\Hyper-V\ISO\Linux\ubuntu-18.04.3-live-server-amd64.iso"
        }
    }
    $VMConfig = "C:\Hyper-V\Config"
    $VHDDest = "C:\Hyper-V\Virtual Hard Disks\"
    $VMDisk = $VHDDest + $VMName + ".vhdx"

    $StartTime = Get-Date
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "Creating $OS Base VM" -ForegroundColor Cyan
    $VM = New-VM -Name $VMName -NewVHDPath $VMDisk -NewVHDSizeBytes 128000000000 -Path $VMConfig -MemoryStartupBytes 4GB -Generation 2
    Set-VM -VM $VM -SnapshotFileLocation $VMConfig -SmartPagingFilePath $VMConfig
    Add-VMScsiController -VMName $VMName
    Set-VMProcessor -VM $VM -Count 4
    Set-VMMemory -VM $VM -DynamicMemoryEnabled $True -MaximumBytes 8GB -MinimumBytes 4GB -StartupBytes 4GB
    Remove-VMNetworkAdapter -VM $VM
    Add-VMNetworkAdapter -VM $VM -Name "PublicNIC" -SwitchName "vs-NIC3" 
    $VMNic = Get-VMNetworkAdapter -VM $VM
    Set-VMNetworkAdapterVlan -VMNetworkAdapter $VMNic -Access -VlanId 7 -ErrorAction Stop
    Enable-VMIntegrationService -VMName $VMName -Name "Guest Service Interface"
    Add-VMDvdDrive -VMName $VMName -Path $ISO
    $MyDVD = Get-VMDvdDrive -VMName $VMName
    switch ($OS) {
        "Server2019" {Set-VMFirmware -VMName $VMName -EnableSecureBoot On -FirstBootDevice $MyDVD}
        default {Set-VMFirmware -VMName $VMName -SecureBootTemplate "MicrosoftUEFICertificateAuthority" -FirstBootDevice $MyDVD}
    }
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "VM Build complete!" -ForegroundColor Cyan
    Write-Host
    Write-Host "Follow the instructions at https://github.com/tracsman/Projects/blob/master/LabModPS/README.md to completed the build of the base VHDX file."
    Write-Host
    
    $EndTime = Get-Date
    $TimeDiff = New-TimeSpan $StartTime $EndTime
    $Mins = $TimeDiff.Minutes
    $Secs = $TimeDiff.Seconds
    $RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs
    Write-Host (Get-Date)' - ' -NoNewline
    Write-Host "VM build completed successfully" -ForegroundColor Green
    Write-Host "Time to create: $RunTime"
    Write-Host
}
