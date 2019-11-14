    $VMName = "UbuntuBase"
    $VMConfig = "C:\Hyper-V\Config"
    $VHDDest = "C:\Hyper-V\Virtual Hard Disks\"
    $VMDisk = $VHDDest + $VMName + ".vhdx"
    $ISO = "C:\Hyper-V\ISO\Linux\ubuntu-18.04.3-live-server-amd64.iso"

$StartTime = Get-Date
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Creating VM" -ForegroundColor Cyan
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
Set-VMFirmware -VMName $VMName -SecureBootTemplate "MicrosoftUEFICertificateAuthority" -FirstBootDevice $MyDVD
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "VM Build complete!" -ForegroundColor Cyan
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Starting the VM..." -ForegroundColor Cyan -NoNewline
Start-VM -Name $VM.Name
Write-Host "Started!" -ForegroundColor Green
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "Waiting for OS...." -ForegroundColor Cyan -NoNewline
Wait-VM -Name $VMName -For Heartbeat
Write-Host "Online!" -ForegroundColor Green

$EndTime = Get-Date
$TimeDiff = New-TimeSpan $StartTime $EndTime
$Mins = $TimeDiff.Minutes
$Secs = $TimeDiff.Seconds
$RunTime = '{0:00}:{1:00} (M:S)' -f $Mins,$Secs
Write-Host (Get-Date)' - ' -NoNewline
Write-Host "$Action completed successfully" -ForegroundColor Green
Write-Host "Time to create: $RunTime"
Write-Host
