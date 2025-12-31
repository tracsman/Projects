function Build-LabBaseVM{

    New-Alias Out-Clipboard $env:SystemRoot\System32\Clip.exe -ErrorAction SilentlyContinue

    $ScriptText = @'
$VMName='Base2025'

Write-Host "Building..........." -NoNewline
Write-Host $VMName -ForegroundColor Yellow
Write-Host "Validating Admin..." -NoNewline
If (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Warning "This script must be run elevated as Administrator!"
    Return
}
Write-Host "Good" -ForegroundColor Green
    
Write-Host "Validating Server.." -NoNewline
$Lab = ($env:COMPUTERNAME.Split("-"))[0]
If (-Not $Lab -eq "SEA" -and -Not $Lab -eq "ASH") {
    Write-Warning "This script must be run on a physical lab server, e.g. SEA-ER-01"
    Return
}
ElseIf ($Lab -eq "ASH") {$SecondOctet=2}
Else {$SecondOctet=1}
Write-Host "Good" -ForegroundColor Green

Write-Host "Setting NIC IP....." -NoNewline
Get-NetFirewallRule -DisplayGroup 'Network Discovery' | Set-NetFirewallRule -Profile 'Private, Domain' -Enabled True -PassThru | Out-Null
Enable-NetFirewallRule -DisplayGroup "Remote Desktop" | Out-Null
If ((Get-NetIPAddress -AddressFamily IPv4 -AddressState Preferred -InterfaceAlias "Ethernet*").IPAddress -ne "10.$SecondOctet.7.120") {
    $nic = Get-NetIPAddress -AddressFamily IPv4 -AddressState Preferred -InterfaceAlias "Ethernet*"
    New-NetIPAddress -InterfaceIndex $nic.InterfaceIndex -IPAddress "10.$SecondOctet.7.120" -PrefixLength 25 -DefaultGateway "10.$SecondOctet.7.1" | Out-Null
    Set-DnsClientServerAddress -InterfaceIndex $nic.InterfaceIndex -ServerAddresses "1.1.1.1", "1.0.0.1" | Out-Null}
Write-Host "Good" -ForegroundColor Green

Write-Host "Check Internet....." -NoNewline
If (-Not (Test-Connection 1.1.1.1 -Count 10 -Quiet)) {
    Write-Warning "Internet Connectivity was not established."
    Write-Warning "Please enable internet connectivity and re-run this script."
    Return}
Write-Host "Good" -ForegroundColor Green

'@
    $ScriptText | Out-Clipboard
    Write-host "Script part 1 of 2 copied to clipboard, paste in Base VM Admin PowerShell ISE, and then return here for part 2."
    Pause

    $ScriptText = @'
Write-Host "Download bits......" -NoNewline
$uri = 'https://raw.githubusercontent.com/tracsman/1DayLab/DontLook/UtilityVM/'
$File = 'MicrosoftEdgeSetup.exe'
$Remote = $uri + $File
$Local = "$HOME\Desktop\" + $File
If (-Not (Test-Path $Local)){
    $webClient = new-object System.Net.WebClient
    $webClient.DownloadFile( $Remote , $Local )}
Write-Host "Good" -ForegroundColor Green
    
Write-Host "Edge Install......." -NoNewline
If (-Not (Test-Path "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")){
    Start-Process -FilePath "$HOME\Desktop\MicrosoftEdgeSetup.exe"}
Write-Host "Good" -ForegroundColor Green
    
Write-Host "Set Environment...." -NoNewline
Set-ItemProperty -Path "HKLM:\System\CurrentControlSet\Control\Terminal Server" -Name "fDenyTSConnections" -Value 0 | Out-Null
Disable-ScheduledTask -TaskName ServerManager -TaskPath "\Microsoft\Windows\Server Manager" | Out-Null
Write-Host "Good" -ForegroundColor Green

Write-Host "Cleaning Up........" -NoNewline
Do {Start-Sleep 1} While ((Get-Process -ProcessName MicrosoftEdgeSetup -ErrorAction SilentlyContinue).Count -gt 0)
Remove-Item "$HOME\Desktop\MicrosoftEdgeSetup.exe" -Force -ErrorAction SilentlyContinue
Clear-RecycleBin -Force
Write-Host "Good" -ForegroundColor Green
    
Write-Host "Renaming VM......." -NoNewline
If ($env:COMPUTERNAME -ne $VMName) {Rename-Computer -NewName "$VMName" -Restart}
Write-Host "Good" -ForegroundColor Green
    
Write-Host "All Done!" -ForegroundColor Green

'@
    $ScriptText | Out-Clipboard
    Write-host "Script part 2 of 2 copied to clipboard, paste in Base VM Admin Powershell ISE, and then run the entire script (part 1 and 2)."
    Write-Host
}

