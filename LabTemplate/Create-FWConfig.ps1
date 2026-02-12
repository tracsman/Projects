# Using customer number as input, create Junos configuration and copy to the clipboard

param(
    [Parameter(Mandatory=$true)]
    [ValidateRange(10, 99)]
    [int]$CustomerNumber
)

$JunosConfigTemplate = @"
set routing-instances Cust$CustomerNumber instance-type virtual-router
set routing-instances Cust$CustomerNumber routing-options interface-routes rib-group inet to-Cust$CustomerNumber-instance
set routing-instances Cust$CustomerNumber routing-options interface-routes rib-group inet6 to-Cust$CustomerNumber-instance-v6
set routing-instances Cust$CustomerNumber routing-options instance-import import-internet-routes
set routing-instances Cust$CustomerNumber protocols bgp group ibgp type internal
set routing-instances Cust$CustomerNumber protocols bgp group ibgp export Cust$CustomerNumber-onprem
set routing-instances Cust$CustomerNumber protocols bgp group ibgp multipath
set routing-instances Cust$CustomerNumber protocols bgp group ibgp neighbor 192.168.$CustomerNumber.0
set routing-instances Cust$CustomerNumber protocols bgp group ibgp neighbor 192.168.$CustomerNumber.2
set routing-instances Cust$CustomerNumber interface reth1.$CustomerNumber
set routing-instances Cust$CustomerNumber interface reth2.$CustomerNumber
set routing-instances Cust$CustomerNumber interface reth3.$CustomerNumber

set interfaces reth1 unit $CustomerNumber vlan-id $CustomerNumber
set interfaces reth1 unit $CustomerNumber family inet address 192.168.$CustomerNumber.1/31

set interfaces reth2 unit $CustomerNumber vlan-id $CustomerNumber
set interfaces reth2 unit $CustomerNumber family inet address 192.168.$CustomerNumber.3/31

set interfaces reth3 unit $CustomerNumber vlan-id $CustomerNumber
set interfaces reth3 unit $CustomerNumber family inet address 10.1.$CustomerNumber.1/25
set interfaces reth3 unit $CustomerNumber family inet6 address 2001:5a0:4406:$CustomerNumber::1/64

set policy-options policy-statement Cust$CustomerNumber-onprem term pvt from interface reth3.$CustomerNumber
set policy-options policy-statement Cust$CustomerNumber-onprem term pvt then accept

set routing-options rib-groups to-Cust$CustomerNumber-instance import-rib inet.0
set routing-options rib-groups to-Cust$CustomerNumber-instance import-rib Cust$CustomerNumber.inet.0
set routing-options rib-groups to-Cust$CustomerNumber-instance-v6 import-rib inet6.0
set routing-options rib-groups to-Cust$CustomerNumber-instance-v6 import-rib Cust$CustomerNumber.inet6.0
"@

# Copy the configuration to clipboard
$JunosConfigTemplate | Set-Clipboard

Write-Host "Junos firewall configuration for Customer $CustomerNumber has been copied to the clipboard!" -ForegroundColor Green
Write-Host ""
Write-Host "Customer Number: " -NoNewline -ForegroundColor Cyan
Write-Host "$CustomerNumber" -ForegroundColor White
Write-Host
Write-Host "Configuration Preview:" -ForegroundColor Cyan
Write-Host $JunosConfigTemplate
