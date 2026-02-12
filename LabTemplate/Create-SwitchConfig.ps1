# Using customer number as input, create Cisco Nexus switch VLAN configuration and copy to the clipboard

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [ValidateRange(10, 99)]
    [int]$CustomerNumber
)

# Generate Cisco Nexus VLAN configuration
$SwitchConfigTemplate = @"
conf t
vlan $CustomerNumber
  name Customer${CustomerNumber}VLAN
end
wr
exit
"@

# Copy the configuration to clipboard
$SwitchConfigTemplate | Set-Clipboard

Write-Host "Cisco Nexus switch VLAN configuration for Customer $CustomerNumber has been copied to the clipboard!" -ForegroundColor Green
Write-Host ""
Write-Host "Customer Number: " -NoNewline -ForegroundColor Cyan
Write-Host "$CustomerNumber" -ForegroundColor White
Write-Host ""
Write-Host "Configuration Preview:" -ForegroundColor Cyan
Write-Host $SwitchConfigTemplate
Write-Host ""
Write-Host "NOTE: This configuration applies to both Cisco Nexus switches." -ForegroundColor Yellow