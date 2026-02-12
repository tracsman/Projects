# Lab Template Deployment Guide

This directory contains scripts and templates for deploying complete customer lab environments with Azure infrastructure and network device configurations.

## Overview

The lab deployment process consists of the following components:

1. **Azure Infrastructure** - Virtual networks, VMs, and ExpressRoute circuits
2. **Network Device Configurations** - Firewall, router, and switch configurations
3. **Equinix Circuit Provisioning** - ExpressRoute circuit provisioning at Equinix
4. **Automated Orchestration** - PowerShell scripts to coordinate the deployment

## Prerequisites

Before deploying a lab, ensure you have:

- **PowerShell 7+** check your $PSVersionTable to ensure you're on 7!!!! ([Download PowerShell](https://github.com/PowerShell/PowerShell/releases))
- **Azure PowerShell** module installed and logged in ([Install Azure PowerShell](https://docs.microsoft.com/en-us/powershell/azure/install-azure-powershell))
- **Appropriate Azure permissions** for resource group creation and ExpressRoute management
- **Customer number** (must be between 10-50)
- **Network access** to configure physical devices (firewall, router, switches)

## Quick Start

### 1. Get an Available Customer Number

Review the existing resource groups in the ExpressRoute-lab subscription, find a good unused number to create this new environment for.

Validate this numbers isn't orphaned in the physical lab:

- on the Firewall run the command (replacing 44 with your desired customer number)

 ```batch
 show configuration | grep Cust44
 ```

- Also RDP to the physical server you're going to place the on-prem VM on. Make sure the server is running and available and that that customer number isn't in use.

### 2. Deploy Azure Infrastructure

Deploy the complete Azure infrastructure for a customer:

```powershell
# Example: Deploy infrastructure for Customer 44 in SEA region
.\Deploy-CustomerLab.ps1 -CustomerNumber 44 -Location SEA

# Or using defaults (SEA location)
.\Deploy-CustomerLab.ps1 -CustomerNumber 44
```

This script will:

- Create the resource group
- Deploy Azure resources using the Bicep template
- Create Key Vault and store credentials
- Provision ExpressRoute circuits
- Wait for circuit provisioning at Equinix
- Create ExpressRoute connections

### 2. Generate Network Device Configurations

Generate and deploy configurations for all network devices using the customer number:

#### Firewall Configuration (Junos)

```powershell
.\Create-FWConfig.ps1 -CustomerNumber 44
```

#### Router Configuration (Junos)

```powershell
# Seattle location with ECX interface (default) for the primary router
.\Create-RouterConfig.ps1 -CustomerNumber 44

# Seattle location with ECX interface (default) for the secondary router
.\Create-RouterConfig.ps1 -CustomerNumber 44 -Peer Secondary
```

#### Switch Configuration (Cisco Nexus)

```powershell
.\Create-SwitchConfig.ps1 -CustomerNumber 44
```

### 3. Apply Network Configurations

Each configuration script copies the generated configuration to your clipboard. Apply them to the respective devices:

1. **Firewall**: Paste configuration into Junos firewall console
2. **Router**: Paste configuration into Junos router console  
3. **Switches**: Apply the same VLAN configuration to both Cisco Nexus switches

## Detailed Deployment Process

### Azure Infrastructure Components

The Bicep template (`Deploy-CustomerLab.bicep`) deploys:

- **Virtual Network**: 10.17.XX.0/24 (SEA) or 10.10.XX.0/24 (ASH)
- **Subnets**:
  - Tenant subnet: 10.17.XX.0/25
  - Gateway subnet: 10.17.XX.128/25
- **Virtual Machine**: Test VM with specified credentials
- **ExpressRoute Circuits**: Primary and secondary circuits for redundancy
- **ExpressRoute Gateway**: VNet gateway for ExpressRoute connectivity

### Network Device Configuration Details

#### Firewall Configuration

- Creates customer-specific virtual router instance
- Configures BGP peering with internal neighbors
- Sets up redundant interfaces (reth1, reth2, reth3)
- IP addressing: 192.168.XX.1/31, 192.168.XX.3/31, 10.1.XX.1/25

#### Router Configuration  

- **Seattle (SEA)**: Junos routers with multiple interface options
  - ECX: xe-0/0/0:0
  - SEA100GbArista: et-0/1/1  
  - SEA100GbJuniper: et-0/1/4
- **Ashburn (ASH)**: Currently uses Cisco routers (not supported by this script)
- Creates customer VRF with BGP peering to Azure
- Supports Primary/Secondary peer configurations

#### Switch Configuration

- Simple VLAN creation using customer number as VLAN ID
- Applies to both Cisco Nexus switches
- Includes descriptive VLAN name and customer identification

### Advanced Options

#### Router Interface Selection

The router script supports different interface options based on location:

**Seattle (SEA)**:

- `ECX` (default) - Standard ECX interface
- `SEA100GbArista` - 100Gb Arista uplink
- `SEA100GbJuniper` - 100Gb Juniper uplink

**Ashburn (ASH)**:

- Currently not supported (uses Cisco routers)

#### Peer Selection

Router configurations support Primary/Secondary peer selection:

- **Primary**: Uses base IP addressing (192.168.XX.17/30)
- **Secondary**: Uses offset IP addressing (192.168.XX.21/30)

## File Structure

```batch
LabTemplate/
├── README.md                   # This file
├── Deploy-CustomerLab.ps1      # Main deployment orchestration script
├── Deploy-CustomerLab.bicep    # Azure infrastructure template
├── Create-FWConfig.ps1         # Firewall configuration generator
├── Create-RouterConfig.ps1     # Router configuration generator  
├── Create-SwitchConfig.ps1     # Switch configuration generator
├── ECX-Action.ps1              # Equinix circuit provisioning
├── Firewall.conf               # Reference firewall configuration
└── Router1.conf                # Reference router configuration
```

## IP Addressing Scheme

The lab uses a structured IP addressing scheme based on the customer number:

- **Azure VNet**: 10.17.XX.0/24 (XX = customer number)
- **Firewall BGP**: 192.168.XX.0, 192.168.XX.2
- **Router Primary Peer**: 192.168.XX.17/30
- **Router Secondary Peer**: 192.168.XX.21/30  
- **VLAN Tags**:
    Customer number × 10 for C-Tag
    Customer number for S-Tag
- **Switch VLAN**: Customer number

## Troubleshooting

### Common Issues

1. **Azure Login**: Ensure you're logged in with `Connect-AzAccount`
2. **Resource Group Naming**: Use format "SEA-CustXX" or "ASH-CustXX"
3. **Customer Number Range**: Must be between 10-99
4. **Clipboard Issues**: Configurations are automatically copied to clipboard for easy pasting

### Verification Steps

After deployment:

1. Verify Azure resources in the portal
2. Check ExpressRoute circuit provisioning status
3. Test network connectivity between on-premises and Azure
4. Validate BGP peering status on network devices

## Support

For issues with:

- **Azure Infrastructure**: Check Azure portal for deployment status
- **Network Configurations**: Verify device console access and configuration syntax
- **Equinix Circuits**: Check ECX portal for circuit status
- **Script Errors**: Review PowerShell execution policy and module dependencies

This completes the deployment of a fully functional customer lab environment with Azure connectivity and network device configurations.
