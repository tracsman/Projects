# Using customer number, location, and interface selection as input, create Junos router configuration and copy to the clipboard

[CmdletBinding(DefaultParameterSetName="SEA")]
param(
    [Parameter(Mandatory=$true, ParameterSetName="SEA")]
    [Parameter(Mandatory=$true, ParameterSetName="ASH")]
    [ValidateRange(10, 99)]
    [int]$CustomerNumber,
    
    [Parameter(Mandatory=$false, ParameterSetName="SEA")]
    [Parameter(Mandatory=$false, ParameterSetName="ASH")]
    [ValidateSet("SEA", "ASH")]
    [string]$Location="SEA",
    
    [Parameter(Mandatory=$false, ParameterSetName="SEA")]
    [Parameter(Mandatory=$false, ParameterSetName="ASH")]
    [ValidateScript({
        $validInterfaces = @{
            "SEA" = @("ECX", "SEA100GbArista", "SEA100GbJuniper")
            "ASH" = @("ECX", "Ash10Gb")
        }
        if ($validInterfaces[$Using:Location] -contains $_) {
            return $true
        } else {
            throw "Interface '$_' is not valid for location '$Using:Location'. Valid interfaces for $Using:Location are: $($validInterfaces[$Using:Location] -join ', ')"
        }
    })]
    [string]$InterfaceOption = "ECX",

    [Parameter(Mandatory=$false, ParameterSetName="SEA")]
    [Parameter(Mandatory=$false, ParameterSetName="ASH")]
    [ValidateSet("Primary", "Secondary")]
    [string]$Peer="Primary"
)

# Interface mapping - friendly names to actual interface names by location
$InterfaceMap = @{
    # Seattle Interfaces
    "SEA_ECX" = "xe-0/0/0:0"
    "SEA_SEA100GbArista" = "et-0/1/1"
    "SEA_SEA100GbJuniper" = "et-0/1/4"
    "SEA_SEA10Gb" = "xe-0/0/1:0"
    
    # Ashburn Interfaces  
    "ASH_ECX" = "xe-0/0/0:0"
    "ASH_Ash10Gb" = "xe-0/0/1:0"
}

# Validate interface option is valid for the selected location
$validInterfaces = @{
    "SEA" = @("ECX", "SEA100GbArista", "SEA100GbJuniper")
    "ASH" = @("ECX", "Ash10Gb")
}

if ($validInterfaces[$Location] -notcontains $InterfaceOption) {
    Write-Error "Interface '$InterfaceOption' is not valid for location '$Location'."
    Write-Host "Valid interfaces for $Location are: $($validInterfaces[$Location] -join ', ')" -ForegroundColor Yellow
    return
}

# Check if ASH location is selected - warn and exit since ASH uses Cisco routers
if ($Location -eq "ASH") {
    Write-Warning "ASH (Ashburn) location is not yet supported by this script."
    Write-Host ""
    Write-Host "NOTICE: ASH location uses Cisco routers with different configuration syntax." -ForegroundColor Yellow
    Write-Host "This script generates Junos configurations for SEA (Seattle) location only." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Parameters provided:" -ForegroundColor Cyan
    Write-Host "  Location:         " -NoNewline -ForegroundColor Cyan
    Write-Host $Location -ForegroundColor White
    Write-Host "  Customer Number:  " -NoNewline -ForegroundColor Cyan
    Write-Host $CustomerNumber -ForegroundColor White
    Write-Host "  Interface Option: " -NoNewline -ForegroundColor Cyan
    Write-Host $InterfaceOption -ForegroundColor White
    Write-Host "  Peer:             " -NoNewline -ForegroundColor Cyan
    Write-Host $Peer -ForegroundColor White
    Write-Host ""
    Write-Host "Please use SEA location for Junos router configurations or contact the network team for ASH Cisco configurations." -ForegroundColor Yellow
    return
}

# Assign router interface based on location and interface option
$ActualInterface = $InterfaceMap["${Location}_${InterfaceOption}"]

# Calculate the inner QnQ tag and IP addresses based on customer number and peer selection
$CTag = $CustomerNumber * 10  # Unit number is customer number * 10

# Calculate IP addresses based on Peer selection
$BaseOffset = $Peer -eq "Primary" ? 0 : 4
$NeighborIP = 18 + $BaseOffset
$InterfaceIP = 17 + $BaseOffset


$JunosRouterConfigTemplate = @"
set routing-instances Cust$CustomerNumber instance-type virtual-router
set routing-instances Cust$CustomerNumber protocols bgp group ibgp type internal
set routing-instances Cust$CustomerNumber protocols bgp group ibgp export nhs-vnet
set routing-instances Cust$CustomerNumber protocols bgp group ibgp neighbor 192.168.$CustomerNumber.1
set routing-instances Cust$CustomerNumber protocols bgp group ebgp peer-as 12076
set routing-instances Cust$CustomerNumber protocols bgp group ebgp bfd-liveness-detection minimum-interval 300
set routing-instances Cust$CustomerNumber protocols bgp group ebgp bfd-liveness-detection multiplier 3
set routing-instances Cust$CustomerNumber protocols bgp group ebgp neighbor 192.168.$CustomerNumber.$NeighborIP
set routing-instances Cust$CustomerNumber description "Customer $CustomerNumber VRF"
set routing-instances Cust$CustomerNumber interface $ActualInterface.$CTag
set routing-instances Cust$CustomerNumber interface ae0.$CustomerNumber

set interfaces $ActualInterface unit $CTag description "Customer $CustomerNumber Private Peering to Azure"
set interfaces $ActualInterface unit $CTag vlan-tags outer $CustomerNumber
set interfaces $ActualInterface unit $CTag vlan-tags inner $CTag
set interfaces $ActualInterface unit $CTag family inet address 192.168.$CustomerNumber.$InterfaceIP/30

set interfaces ae0 unit $CustomerNumber vlan-id $CustomerNumber
set interfaces ae0 unit $CustomerNumber family inet address 192.168.$CustomerNumber.0/31
"@

# Copy the configuration to clipboard
$JunosRouterConfigTemplate | Set-Clipboard

Write-Host "Junos router configuration for Customer $CustomerNumber in $Location using $InterfaceOption ($ActualInterface) has been copied to the clipboard!" -ForegroundColor Green
Write-Host ""
Write-Host "Customer Number: " -NoNewline -ForegroundColor Cyan
Write-Host "$CustomerNumber" -ForegroundColor White
Write-Host "Location:        " -NoNewline -ForegroundColor Cyan
Write-Host "$Location" -ForegroundColor White
Write-Host "Peer:            " -NoNewline -ForegroundColor Cyan
Write-Host "$Peer" -ForegroundColor White
Write-Host "Interface:       " -NoNewline -ForegroundColor Cyan
Write-Host $InterfaceOption -ForegroundColor White -NoNewline
Write-Host " -> " -NoNewline -ForegroundColor Cyan
Write-Host $ActualInterface -ForegroundColor White
Write-Host ""
Write-Host "Configuration Preview:" -ForegroundColor Cyan
Write-Host $JunosRouterConfigTemplate
