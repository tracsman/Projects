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

- Also RDP to the physical server you're going to place the on-prem VM on. Make sure the server is running and available and that that customer number isn't in use. Customer 44 VMs should be on Server 4 (ie the first digit of the customer number is usually the physical server number)

### 2. Deploy Azure Infrastructure

Deploy the complete Azure infrastructure for a customer:

```powershell
# Example: Deploy infrastructure for Customer 44 in Seattle lab

.\Deploy-CustomerLab.ps1 -CustomerNumber 44 
```

This script will:

- Create the resource group
- Deploy Azure resources using the Bicep template
- Create Key Vault and store credentials
- Create ExpressRoute circuits
- Wait for circuit provisioning at Equinix
- Kick off ER Gateway creation
- Create a VNET and VM (Ubuntu)
- Create ExpressRoute connections (when GW deployment created)

### 3. Generate Network Device Configurations

Generate and deploy configurations for all network devices using the customer number:

#### Firewall Configuration (Junos)

```powershell
.\Create-FWConfig.ps1 -CustomerNumber 44
```

Deploy to **SEA-SRX42-01**, a Juniper SRX4200 firewall. Note this is actually two firewalls but they are clustered so updating one, updates both.

#### Router Configuration (Junos)

```powershell
# Seattle location with ECX interface (default) for the primary router
.\Create-RouterConfig.ps1 -CustomerNumber 44
```

```powershell
# Seattle location with ECX interface (default) for the secondary router
.\Create-RouterConfig.ps1 -CustomerNumber 44 -Peer Secondary
```

The routers are MX10003 Juniper routers, same SKU and components as our MSEEs.<br/>
Primary router is **SEA-MX03-01**<br/>
Secondary router is **SEA-MX03-02**

**Important** make sure you're putting the right config on the right router (ie don't mix up primary and secondary config and routers)

#### Switch Configuration (Cisco Nexus)

```powershell
.\Create-SwitchConfig.ps1 -CustomerNumber 44
```

The switches are Cisco Nexus 9k switches, and although they are L3 switches we treat them as L2 "dumb" switches, so the config is only adding VLANs with no IP addresses so the config is exactly the same for both devices.

This config should be deployed to **SEA-NX9K-01** and **SEA-NX9K-01**.

### 4. Create On-Prem VM

**NOTE**: The Azure build, at least the Key Vault, must be complete before starting this step. The Key Vault is needed to get the VM Password.

- RDP to the physical server you want to create the VM
- Open an Admin PowerShell prompt (must be PS 7)

```powershell
New-LabVM 44 -OS Ubuntu
```

- You'll be prompted for the Admin Password and the PathLabUser password.
  - Admin Password: ([Server-Admin](https://ms.portal.azure.com/?feature.enableIPv6VpnGateway=true#view/Microsoft_Azure_KeyVault/ListObjectVersionsRBACBlade/~/overview/objectType/secrets/objectId/https%3A%2F%2Flabsecrets.vault.azure.net%2Fsecrets%2FServer-Admin/vaultResourceUri/%2Fsubscriptions%2F4bffbb15-d414-4874-a2e4-c548c6d45e2a%2FresourceGroups%2FLabInfrastructure%2Fproviders%2FMicrosoft.KeyVault%2Fvaults%2FLabSecrets/vaultId/%2Fsubscriptions%2F4bffbb15-d414-4874-a2e4-c548c6d45e2a%2FresourceGroups%2FLabInfrastructure%2Fproviders%2FMicrosoft.KeyVault%2Fvaults%2FLabSecrets/lifecycleState~/null))
  - PathLabUser password: Go to the Secrets in the Key Vault for this Customer (e.g. SEA-Cust44-kv)

### 5. Lab Validation

Once everything is complete, you can SSH to the On-Prem VM over the internet, and then ping the Azure VM over ExpressRoute.

To this:

- SSH to the On-prem VM:

```bash
ssh PathLabUser@sea.pathlab.xyz -p 4410
```

Where 4410 is the Customer NUmber (44) and IP of the VM (10 is always the first IP on-prem)

- Use the PathLabUser password from the Key Vault. Note: when you paste in the password you won't see ***'s or anything, just trust the right-click pastes.
- Say yes to save the VM thumbprint
- on the VM now ping the Azure VM

```bash
ping 10.17.44.4
```

Where the third octet (44 in this example) is the customer number.

The first two pings may fail, but after that should work. If not, something screwed up. If ping works, you're done and you can sent the information to the requestor to access the lab.

### 6. Notify users of access instructions and details


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
