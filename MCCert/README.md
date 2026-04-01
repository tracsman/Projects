# Multi-VNet Infrastructure Deployment for Azure China

This Bicep template deploys a multi-VNet infrastructure in Azure China (China North 3) with VPN connectivity, automated VPN scheduling, and NSG-based traffic control.

## Architecture

![Architecture Diagram](media/Setup.vsdx)

## Overview

The deployment creates three virtual networks with the following components:

- **VNet01 (Hub)**: Central hub with VPN Gateway for P2S and S2S connectivity, NSG with ICMP rules
- **VNet02 (Spoke)**: Windows 11 workstation accessible via Azure Bastion
- **VNet03 (Spoke)**: Linux VMs with automated strongSwan-based VPN router for S2S connection
- **VMSS**: Single Virtual Machine Scale Set managing all VM instances with Flexible orchestration
- **Automation**: Automation Account with scheduled VPN connect/disconnect runbook

## Network Topology

| VNet | Address Space | Purpose |
| ---- | ------------- | ------- |
| VNet01 | 10.1.0.0/24 | Hub - VPN Gateway, Ubuntu VM, NSG |
| VNet02 | 10.2.0.0/24 | Spoke - Windows 11 & Bastion |
| VNet03 | 10.3.0.0/24 | Spoke - Ubuntu VMs & strongSwan Router |

## Subnets

| VNet | Subnet Name | Address Range | Purpose |
| ---- | ----------- | ------------- | ------- |
| VNet01 | Tenant | 10.1.0.0/25 | Workload VMs (NSG attached) |
| VNet01 | GatewaySubnet | 10.1.0.128/25 | VPN Gateway |
| VNet02 | Tenant | 10.2.0.0/25 | Workload VMs |
| VNet02 | AzureBastionSubnet | 10.2.0.128/25 | Azure Bastion |
| VNet03 | Tenant | 10.3.0.0/25 | Workload VMs (Route Table attached) |
| VNet03 | VPN | 10.3.0.128/25 | strongSwan Router |

## Resources

### VNet01 Resources

| Resource | Name | Type |
| -------- | ---- | ---- |
| Virtual Network | VNet01 | Microsoft.Network/virtualNetworks |
| VPN Gateway | VNet01-gw-vpn | Microsoft.Network/virtualNetworkGateways |
| VPN Gateway Public IP | VNet01-gw-vpn-pip | Microsoft.Network/publicIPAddresses |
| NSG | VNet01-nsg | Microsoft.Network/networkSecurityGroups |
| Ubuntu VM (VMSS) | VNet01-VM01 | Microsoft.Compute/virtualMachines |
| VM NIC | VNet01-VM01-nic | Microsoft.Network/networkInterfaces |

### VNet02 Resources

| Resource | Name | Type |
| -------- | ---- | ---- |
| Virtual Network | VNet02 | Microsoft.Network/virtualNetworks |
| Bastion Host | VNet02-bastion | Microsoft.Network/bastionHosts |
| Bastion Public IP | VNet02-bastion-pip | Microsoft.Network/publicIPAddresses |
| Windows 11 VM (VMSS) | VNet02-VM01 | Microsoft.Compute/virtualMachines |
| VM NIC | VNet02-VM01-nic | Microsoft.Network/networkInterfaces |

### VNet03 Resources

| Resource | Name | Type |
| -------- | ---- | ---- |
| Virtual Network | VNet03 | Microsoft.Network/virtualNetworks |
| Route Table | VNet03-tenant-rt | Microsoft.Network/routeTables |
| Ubuntu VM (VMSS) | VNet03-VM01 | Microsoft.Compute/virtualMachines |
| VM NIC | VNet03-VM01-nic | Microsoft.Network/networkInterfaces |
| Ubuntu VM (VMSS) | VNet03-VM02 | Microsoft.Compute/virtualMachines |
| VM NIC | VNet03-VM02-nic | Microsoft.Network/networkInterfaces |
| strongSwan Router (VMSS) | VNet03-router01 | Microsoft.Compute/virtualMachines |
| Router NIC (IP forwarding) | VNet03-router01-nic | Microsoft.Network/networkInterfaces |
| Router Public IP | VNet03-router01-pip | Microsoft.Network/publicIPAddresses |

### Security & Identity Resources

| Resource | Name | Purpose |
| -------- | ---- | ------- |
| Key Vault (RBAC) | jonor-{uniqueString} | Secure storage for VM passwords and VPN keys |
| Secret | vm-admin-password | Admin password for all VMs |
| Secret | vpn-shared-key | Pre-shared key for S2S VPN |
| NSG Rule | Allow-ICMPv4-From-VNet03-VM01 | Allow ICMP from VNet03-VM01 to VNet01-VM01 |
| NSG Rule | Deny-ICMPv4-From-VNet03-VM02 | Deny ICMP from VNet03-VM02 to VNet01-VM01 |
| Managed Identity | vpnControl-mi | User-assigned identity for deployment scripts |

### VPN Resources

| Resource | Name | Purpose |
| -------- | ---- | ------- |
| Local Network Gateway | VNet01-lng-vnet03-router | Represents VNet03 strongSwan router |
| VPN Connection | VNet01-conn-vnet03-s2s | S2S IPsec/IKEv2 tunnel |
| Deployment Script | VNet03-router01-strongswan-config | Configures strongSwan via `az vm run-command` |

### Automation Resources

| Resource | Name | Purpose |
| -------- | ---- | ------- |
| Automation Account | vpnAutomation | Hosts runbook and schedules (System-assigned MI) |
| Runbook | vpnControl | PowerShell script to connect/disconnect S2S VPN |
| Deployment Script | vpnControl-Publish | Publishes runbook content via REST API |
| Schedule | vpn-connect-daily-9am | Daily 9:00 AM CST - connects VPN |
| Schedule | vpn-disconnect-daily-5pm | Daily 5:00 PM CST - disconnects VPN |
| Role Assignment | Network Contributor (RG) | Automation MI manages VPN connections |
| Role Assignment | Key Vault Secrets User (KV) | Automation MI reads VPN shared key |
| Role Assignment | Virtual Machine Contributor (RG) | Automation MI restarts strongSwan on router |
| Role Assignment | Automation Contributor (AA) | Deployment script MI publishes runbooks |

### VMSS & Monitoring Resources

| Resource | Name | Purpose |
| -------- | ---- | ------- |
| Virtual Machine Scale Set | VPNGWJonVMSS | Flexible orchestration for all VMs |
| Log Analytics Workspace | jonorLogs | Centralized logging |

## VPN Configuration

### Point-to-Site (P2S) VPN

| Setting | Value |
| ------- | ----- |
| Authentication | Entra ID (Azure AD) via China cloud endpoints |
| Protocol | OpenVPN |
| Client Address Pool | 172.16.0.0/24 |
| NAT Pool | 192.168.10.0/24 |
| Gateway SKU | VpnGw2AZ (Generation 2) |

### Site-to-Site (S2S) VPN

| Setting | Value |
| ------- | ----- |
| Authentication | Pre-Shared Key (PSK) |
| Protocol | IKEv2 |
| Local Network | 10.3.0.0/24 (VNet03) |
| Router | strongSwan on Ubuntu 22.04, static IP 10.3.0.132 |
| IKE | aes256-sha1-modp1024 |
| ESP | aes256-sha1 |

### VPN Automation (vpnControl Runbook)

The `vpnControl` runbook manages the S2S VPN connection on a daily schedule:

| Action | Schedule | What It Does |
| ------ | -------- | ------------ |
| **On** | 9:00 AM CST daily | Restores shared key from Key Vault, restarts strongSwan on router |
| **Off** | 5:00 PM CST daily | Rotates shared key to random value, tunnel drops |

The runbook includes PSK rotation for security (randomized key on disconnect, restored from Key Vault on connect). On connect, it remotely restarts strongSwan on VNet03-router01 via `Invoke-AzVMRunCommand` to re-initiate the tunnel.

## VM Specifications

| VM Name | OS | Size | Purpose | OS Disk |
| ------- | -- | ---- | ------- | ------- |
| VNet01-VM01 | Ubuntu 22.04 LTS | Standard_B1s | Workload | VNet01-VM01-OSDisk |
| VNet02-VM01 | Windows 11 23H2 Pro | Standard_B2s | P2S VPN Client | VNet02-VM01-OSDisk |
| VNet03-VM01 | Ubuntu 22.04 LTS | Standard_B1s | Workload | VNet03-VM01-OSDisk |
| VNet03-VM02 | Ubuntu 22.04 LTS | Standard_B1s | Workload | VNet03-VM02-OSDisk |
| VNet03-router01 | Ubuntu 22.04 LTS | Standard_B2s | S2S VPN Router | VNet03-router01-OSDisk |

All VMs are managed through a Flexible VMSS (`VPNGWJonVMSS`) while preserving friendly custom names.

## Key Features

- **Auto-Detection**: Automatically detects deployment phase, gateway existence, and existing resources
- **Smart Secrets**: Only prompts for passwords when needed (auto-reads from Key Vault)
- **Automated strongSwan**: Fully automated S2S VPN configuration via deployment script
- **VPN Scheduling**: Daily connect at 9 AM / disconnect at 5 PM via Automation runbook
- **PSK Rotation**: Shared key randomized on disconnect, restored from Key Vault on connect
- **NSG Traffic Control**: ICMP allow/deny rules between VNet03 VMs and VNet01-VM01
- **RBAC Key Vault**: Modern Azure RBAC instead of legacy access policies
- **Gateway Skip**: `deployGateway` parameter references existing gateway to speed up redeployments
- **Friendly Names**: All VMs and disks have meaningful names (no auto-generated names)
- **One-Command Deployment**: `./deploy.ps1` handles everything intelligently

## Prerequisites

- Azure China subscription
- Azure PowerShell module (`Az`)
- Entra ID Tenant ID
- Bicep CLI (included with Azure CLI / Az PowerShell)

## Deployment

### Simple Deployment (Recommended)

```powershell
# Connect to Azure China
Connect-AzAccount -Environment AzureChinaCloud

# Navigate to project folder
cd c:\Bin\Git\Projects\MCCert

# Auto-detecting deployment - just run it!
./deploy.ps1
```

The script automatically:

- Detects deployment phase (Infrastructure → VMs → Updates)
- Detects existing VPN Gateway (skips slow re-validation)
- Finds existing secrets in Key Vault (no re-prompting)
- Checks Log Analytics workspace existence
- Prompts only when needed (first run or missing secrets)

### Manual Overrides (Optional)

```powershell
./deploy.ps1 -DeployVMs $false                    # Force Phase 1 (Infrastructure only)
./deploy.ps1 -DeployVMs $true                     # Force Phase 2 (VMs)
./deploy.ps1 -DeployGateway $false                # Skip gateway (use existing)
./deploy.ps1 -DeployVMs $true -DeployGateway $false  # VMs only, skip gateway
```

### Deployment Phases

#### Phase 1 - Infrastructure (Auto-detected: No Key Vault exists)

- Creates VNets, VPN Gateway, Key Vault, Bastion, Log Analytics, Automation Account
- Prompts for admin password & VPN shared key (stored in Key Vault)
- Time: ~40-55 minutes (VPN Gateway provisioning is slow)

#### Phase 2 - VMs (Auto-detected: Key Vault exists)

- Creates VMSS, all VMs, NICs, NSG, route table, S2S VPN connection
- Configures strongSwan router via deployment script
- Publishes vpnControl runbook and links schedules
- Uses existing secrets from Key Vault automatically
- Time: ~8-15 minutes (faster if gateway already exists)

#### Updates/Maintenance (Auto-detected: Both Key Vault and VMs exist)

- Updates existing deployment with any template changes
- Uses stored secrets automatically

### Parameters

| Parameter | Auto-Detected? | Description |
| --------- | -------------- | ----------- |
| **deployVMs** | Yes | Controls VM deployment phase |
| **deployGateway** | Yes | Skip gateway on redeployments |
| **createLogWorkspace** | Yes | Creates or references Log Analytics |
| **adminPassword** | Smart | VM admin password (from KV if available) |
| **vpnSharedKey** | Smart | VPN pre-shared key (from KV if available) |
| **aadTenantId** | Param file | Entra ID Tenant GUID |
| **aadAudience** | Param file | Azure VPN Enterprise App ID |

## Post-Deployment

### 1. Verify Deployment Outputs

```powershell
$deployment = Get-AzResourceGroupDeployment -ResourceGroupName "VPN-jonor" |
  Sort-Object Timestamp -Descending | Select-Object -First 1
$deployment.Outputs
```

### 2. Verify S2S VPN (Fully Automated)

```bash
# SSH to VNet03-router01 and check tunnel
sudo ipsec statusall

# Test connectivity to VNet01
ping 10.1.0.4

# Check service status
sudo systemctl status strongswan-starter
```

### 3. Configure P2S VPN Client

1. Azure Portal → VNet01-gw-vpn → Point-to-site configuration
2. Download VPN client
3. Install on Windows 11 VM (VNet02-VM01) or local machine

### 4. Access Windows VM via Bastion

1. Azure Portal → VNet02-VM01 → Connect → Bastion
2. Username: `azureuser`, enter your password

### 5. Verify VPN Automation

```powershell
# Check schedules
Get-AzAutomationSchedule -ResourceGroupName "VPN-jonor" -AutomationAccountName "vpnAutomation"

# Manually trigger the runbook
Start-AzAutomationRunbook -ResourceGroupName "VPN-jonor" -AutomationAccountName "vpnAutomation" `
  -Name "vpnControl" -Parameters @{Action = "On"}
```

## Estimated Deployment Time

| Phase | Resources | Time |
| ----- | --------- | ---- |
| Phase 1 | VNets, Key Vault, Bastion, VPN Gateway, Automation | ~40-55 min |
| Phase 2 | VMSS, VMs (5x), NSG, S2S VPN, strongSwan, Runbook | ~8-15 min |
| Update (no gateway) | Incremental changes | ~3-8 min |

## Cost Considerations

| Resource | SKU | Estimated Monthly Cost |
| -------- | --- | ---------------------- |
| VPN Gateway | VpnGw2AZ | ~$300 USD |
| Azure Bastion | Basic | ~$140 USD |
| VMSS (5 instances) | B1s/B2s | ~$60-120 USD |
| Public IPs (3x) | Standard | ~$15 USD |
| Automation Account | Basic | Free (500 min/month) |
| Key Vault | Standard | ~$1 USD |
| Log Analytics | Per-GB | Usage-based |

*Costs are estimates for Azure China. Actual costs may vary.*

## Files

| File | Description |
| ---- | ----------- |
| main.bicep | Main Bicep template (all infrastructure + automation) |
| main.bicepparam | Parameter values (tenant ID, audience, location) |
| deploy.ps1 | PowerShell deployment script with auto-detection |
| media/Setup.vsdx | Architecture diagram (Visio) |

## Troubleshooting

### Deployment script prompts for passwords when Key Vault secrets should exist

- Ensure RBAC permissions to read Key Vault secrets
- Check secrets exist: `Get-AzKeyVaultSecret -VaultName <kvname>`
- Verify Key Vault name follows pattern `jonor-*` in resource group `VPN-jonor`

### VPN Gateway deployment takes too long

- VPN Gateway can take up to 45 minutes (normal for VpnGw2AZ)
- On subsequent deploys, use `-DeployGateway $false` or let auto-detection skip it
- GatewaySubnet is /25 (well above the /27 minimum)

### S2S VPN not establishing

- **Check strongSwan config script** completed in Azure Portal → Deployment scripts
- **Verify service:** `sudo systemctl status strongswan-starter`
- **Check tunnel:** `sudo ipsec statusall`
- **View logs:** `sudo journalctl -u strongswan-starter -f`
- **Restart tunnel:** `sudo ipsec restart && sudo ipsec up azure-vnet01`
- **Azure side:** Portal → VNet01-gw-vpn → Connections → status should be "Connected"

### Cannot connect P2S VPN

- Verify Entra ID Tenant ID is correct in main.bicepparam
- Ensure Azure VPN Enterprise App is available in your tenant
- For Azure China, AAD endpoints use `login.chinacloudapi.cn` and `sts.chinacloudapi.cn`

### vpnControl runbook publish fails

- Verify `vpnControl-mi` managed identity exists and has Automation Contributor role
- Check deployment script logs in Azure Portal → Deployment scripts → vpnControl-Publish
- The script uses REST API (`Invoke-AzRestMethod`) to upload and publish (avoids cmdlet bugs)

### VPN automation schedule not working

- Check runbook is in "Published" state in Automation Account
- Verify schedules exist: Portal → vpnAutomation → Schedules
- Check job history: Portal → vpnAutomation → Jobs
- Ensure Automation Account managed identity has Network Contributor and Key Vault Secrets User roles

### VMs cannot communicate between VNets

- Verify S2S VPN tunnel is established
- Check route table `VNet03-tenant-rt` is applied to VNet03 Tenant subnet
- Test from router: `ping 10.1.0.4` (from VNet03-router01)
- Verify IP forwarding: `cat /proc/sys/net/ipv4/ip_forward` should return `1`
- Check NSG rules on VNet01-nsg (ICMP allowed from VM01, denied from VM02)

## License

MIT
