// ============================================================================
// Multi-VNet Infrastructure Deployment for Azure China (China North 3)
// ============================================================================
// VNet01: Hub VNet with VPN Gateway, Ubuntu VM
// VNet02: Spoke VNet with Windows 11 VM and Bastion
// VNet03: Spoke VNet with Ubuntu VM and strongSwan Router
// ============================================================================

targetScope = 'resourceGroup'

// ============================================================================
// Parameters
// ============================================================================

@description('Location for all resources')
param location string = 'chinanorth3'

@description('Admin username for all VMs')
param adminUsername string = 'azureuser'

@description('Entra ID Tenant ID for P2S VPN authentication')
param aadTenantId string = subscription().tenantId

@description('Entra ID Audience (Application ID) for P2S VPN - Use Azure VPN Enterprise App ID')
param aadAudience string = 'c632b3df-fb67-4d84-bdcf-b95ad541b5c8'

@description('Key Vault name for storing secrets')
param keyVaultName string = 'jonor-${uniqueString(resourceGroup().id)}'

@description('Admin password secret name in Key Vault')
param adminPasswordSecretName string = 'vm-admin-password'

@description('VPN shared key secret name in Key Vault')
param vpnSharedKeySecretName string = 'vpn-shared-key'

@description('Deploy VPN Gateway (false to reference existing and skip slow validation)')
param deployGateway bool = true

@description('Deploy VMs (set to false for initial Key Vault setup)')
param deployVMs bool = false

@description('Create new Log Analytics workspace (false to reference existing)')
param createLogWorkspace bool = true

@description('Admin password for VMs')
@secure()
param adminPassword string

@description('VPN shared key for S2S connection')
@secure()
param vpnSharedKey string

// ============================================================================
// Variables
// ============================================================================

// VNet01 Configuration
var vnet01Name = 'VNet01'
var vnet01AddressSpace = '10.1.0.0/24'
var vnet01TenantSubnetName = 'Tenant'
var vnet01TenantSubnetPrefix = '10.1.0.0/25'
var vnet01GatewaySubnetPrefix = '10.1.0.128/25'

// VNet02 Configuration
var vnet02Name = 'VNet02'
var vnet02AddressSpace = '10.2.0.0/24'
var vnet02TenantSubnetName = 'Tenant'
var vnet02TenantSubnetPrefix = '10.2.0.0/25'
var vnet02BastionSubnetPrefix = '10.2.0.128/25'

// VNet03 Configuration
var vnet03Name = 'VNet03'
var vnet03AddressSpace = '10.3.0.0/24'
var vnet03TenantSubnetName = 'Tenant'
var vnet03TenantSubnetPrefix = '10.3.0.0/25'
var vnet03VpnSubnetName = 'VPN'
var vnet03VpnSubnetPrefix = '10.3.0.128/25'
// Static IP for VNet03 router (to break circular dependency)
var vnet03RouterStaticIp = '10.3.0.132'

// P2S VPN Configuration
var p2sClientAddressPool = '172.16.0.0/24'
var p2sNatPool = '192.168.10.0/24'

// Entra ID Issuer URL (China cloud uses different endpoint)
var aadIssuer = 'https://sts.chinacloudapi.cn/${aadTenantId}/'

// ============================================================================
// Key Vault for Secrets Storage
// ============================================================================

resource keyVault 'Microsoft.KeyVault/vaults@2024-04-01-preview' = {
  name: keyVaultName
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: subscription().tenantId
    enabledForDeployment: true
    enabledForTemplateDeployment: true
    enabledForDiskEncryption: false
    enableRbacAuthorization: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Key Vault Secrets with actual values from parameters
resource adminPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: adminPasswordSecretName
  properties: {
    value: adminPassword
    contentType: 'VM Admin Password'
  }
}

resource vpnSharedKeySecret 'Microsoft.KeyVault/vaults/secrets@2024-04-01-preview' = {
  parent: keyVault
  name: vpnSharedKeySecretName
  properties: {
    value: vpnSharedKey
    contentType: 'VPN S2S Shared Key'
  }
}

// ============================================================================
// Log Analytics Workspace
// ============================================================================

// Create new Log Analytics workspace
resource logAnalyticsWorkspaceNew 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (createLogWorkspace) {
  name: 'jonorLogs'
  location: location
  properties: {
    sku: {
      name: 'pergb2018'
    }
    retentionInDays: 30
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
    workspaceCapping: {
      dailyQuotaGb: -1
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// Reference existing Log Analytics workspace
resource logAnalyticsWorkspaceExisting 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = if (!createLogWorkspace) {
  name: 'jonorLogs'
}

// Deployment script to validate Key Vault secrets exist (only for Phase 1)
resource secretValidationScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = if (deployVMs == false) {
  name: 'validate-keyvault-secrets'
  location: location
  kind: 'AzureCLI'
  properties: {
    azCliVersion: '2.50.0'
    timeout: 'PT5M'
    retentionInterval: 'PT1H'
    environmentVariables: [
      {
        name: 'KEY_VAULT_NAME'
        value: keyVault.name
      }
      {
        name: 'ADMIN_SECRET_NAME'
        value: adminPasswordSecretName
      }
      {
        name: 'VPN_SECRET_NAME'
        value: vpnSharedKeySecretName
      }
    ]
    scriptContent: '''
      echo "Key Vault secrets validation for Phase 1..."
      
      # Check if admin password secret exists
      if ! az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$ADMIN_SECRET_NAME" --query "id" -o tsv > /dev/null 2>&1; then
        echo "ERROR: Secret '$ADMIN_SECRET_NAME' not found in Key Vault '$KEY_VAULT_NAME'"
        exit 1
      fi
      
      # Check if VPN shared key secret exists  
      if ! az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$VPN_SECRET_NAME" --query "id" -o tsv > /dev/null 2>&1; then
        echo "ERROR: Secret '$VPN_SECRET_NAME' not found in Key Vault '$KEY_VAULT_NAME'"
        exit 1
      fi
      
      echo "Phase 1: Key Vault secrets created successfully!"
    '''
  }
}

// ============================================================================
// VNet01 - Hub VNet with VPN Gateway
// ============================================================================

resource vnet01 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnet01Name
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnet01AddressSpace
      ]
    }
    subnets: [
      {
        name: vnet01TenantSubnetName
        properties: {
          addressPrefix: vnet01TenantSubnetPrefix
        }
      }
      {
        name: 'GatewaySubnet'
        properties: {
          addressPrefix: vnet01GatewaySubnetPrefix
        }
      }
    ]
  }
}

// VNet01 VPN Gateway Public IP
resource vnet01GwPip 'Microsoft.Network/publicIPAddresses@2024-01-01' = if (deployGateway) {
  name: '${vnet01Name}-gw-vpn-pip'
  location: location
  sku: {
    name: 'Standard'
  }
  zones: [
    '1'
  ]
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

resource vnet01GwPipExisting 'Microsoft.Network/publicIPAddresses@2024-01-01' existing = if (!deployGateway) {
  name: '${vnet01Name}-gw-vpn-pip'
}

// VNet01 VPN Gateway
resource vnet01VpnGateway 'Microsoft.Network/virtualNetworkGateways@2024-01-01' = if (deployGateway) {
  name: '${vnet01Name}-gw-vpn'
  location: location
  properties: {
    gatewayType: 'Vpn'
    vpnType: 'RouteBased'
    vpnGatewayGeneration: 'Generation2'
    sku: {
      name: 'VpnGw2AZ'
      tier: 'VpnGw2AZ'
    }
    activeActive: false
    enableBgp: false
    ipConfigurations: [
      {
        name: 'default'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: '${vnet01.id}/subnets/GatewaySubnet'
          }
          publicIPAddress: {
            id: vnet01GwPip.id
          }
        }
      }
    ]
    vpnClientConfiguration: {
      vpnClientAddressPool: {
        addressPrefixes: [
          p2sClientAddressPool
        ]
      }
      vpnClientProtocols: [
        'OpenVPN'
      ]
      vpnAuthenticationTypes: [
        'AAD'
      ]
      aadTenant: 'https://login.chinacloudapi.cn/${aadTenantId}/'
      aadAudience: aadAudience
      aadIssuer: aadIssuer
    }
    natRules: [
      {
        name: 'P2S-NAT-Rule'
        properties: {
          type: 'Static'
          mode: 'IngressSnat'
          internalMappings: [
            {
              addressSpace: p2sClientAddressPool
            }
          ]
          externalMappings: [
            {
              addressSpace: p2sNatPool
            }
          ]
        }
      }
    ]
  }
}

resource vnet01VpnGatewayExisting 'Microsoft.Network/virtualNetworkGateways@2024-01-01' existing = if (!deployGateway) {
  name: '${vnet01Name}-gw-vpn'
}

// Unified references for gateway resources
var vpnGatewayId = deployGateway ? vnet01VpnGateway.id : vnet01VpnGatewayExisting.id
var gwPipIpAddress = deployGateway ? vnet01GwPip.properties.ipAddress : vnet01GwPipExisting.properties.ipAddress

// ============================================================================
// VNet01 NSG - Tenant Subnet
// ============================================================================

resource vnet01Nsg 'Microsoft.Network/networkSecurityGroups@2024-01-01' = if (deployVMs) {
  name: 'VNet01-nsg'
  location: location
  properties: {
    securityRules: [
      {
        name: 'Allow-ICMPv4-From-VNet03-VM01'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Icmp'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: vnet03Vm01Nic.properties.ipConfigurations[0].properties.privateIPAddress
          destinationAddressPrefix: vnet01Vm01Nic.properties.ipConfigurations[0].properties.privateIPAddress
        }
      }
      {
        name: 'Deny-ICMPv4-From-VNet03-VM02'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Deny'
          protocol: 'Icmp'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: vnet03Vm02Nic.properties.ipConfigurations[0].properties.privateIPAddress
          destinationAddressPrefix: vnet01Vm01Nic.properties.ipConfigurations[0].properties.privateIPAddress
        }
      }
    ]
  }
}

// Update VNet01 Tenant subnet to attach NSG
resource vnet01TenantSubnetUpdate 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = if (deployVMs) {
  parent: vnet01
  name: vnet01TenantSubnetName
  properties: {
    addressPrefix: vnet01TenantSubnetPrefix
    networkSecurityGroup: {
      id: vnet01Nsg.id
    }
  }
}

// ============================================================================
// Virtual Machine Scale Set - All VMs  
// ============================================================================

// VMSS for all VMs (mixed OS support with Flexible orchestration)
resource vmss 'Microsoft.Compute/virtualMachineScaleSets@2024-11-01' = if (deployVMs) {
  name: 'VPNGWJonVMSS'
  location: location
  sku: {
    name: 'Standard_B2s'
    tier: 'Standard'
  }
  properties: {
    singlePlacementGroup: false
    orchestrationMode: 'Flexible'
    upgradePolicy: {
      mode: 'Manual'
    }
    scaleInPolicy: {
      rules: [
        'Default'
      ]
      forceDeletion: false
    }
    // Remove platformFaultDomainCount to allow custom VM names
    constrainedMaximumCapacity: false
  }
}

// VNet01 VM NIC
resource vnet01Vm01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = if (deployVMs) {
  name: 'VNet01-VM01-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: '${vnet01.id}/subnets/${vnet01TenantSubnetName}'
          }
        }
      }
    ]
  }
}

// VNet01 Ubuntu VM Instance (VMSS)
resource vnet01VM 'Microsoft.Compute/virtualMachines@2024-11-01' = if (deployVMs) {
  name: 'VNet01-VM01'
  location: location
  properties: {
    virtualMachineScaleSet: {
      id: vmss.id
    }
    hardwareProfile: {
      vmSize: 'Standard_B1s'
    }
    osProfile: {
      computerName: 'VNet01-VM01'
      adminUsername: adminUsername
      adminPassword: adminPassword
      linuxConfiguration: {
        disablePasswordAuthentication: false
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        name: 'VNet01-VM01-OSDisk'
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'Standard_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: vnet01Vm01Nic.id
          properties: {
            primary: true
          }
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

// ============================================================================
// VNet02 - Spoke VNet with Windows 11 VM and Bastion
// ============================================================================

resource vnet02 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnet02Name
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnet02AddressSpace
      ]
    }
    subnets: [
      {
        name: vnet02TenantSubnetName
        properties: {
          addressPrefix: vnet02TenantSubnetPrefix
        }
      }
      {
        name: 'AzureBastionSubnet'
        properties: {
          addressPrefix: vnet02BastionSubnetPrefix
        }
      }
    ]
  }
}

// VNet02 Bastion Public IP
resource vnet02BastionPip 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
  name: '${vnet02Name}-bastion-pip'
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

// VNet02 Bastion Host
resource vnet02Bastion 'Microsoft.Network/bastionHosts@2024-01-01' = {
  name: '${vnet02Name}-bastion'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    ipConfigurations: [
      {
        name: 'IpConf'
        properties: {
          subnet: {
            id: '${vnet02.id}/subnets/AzureBastionSubnet'
          }
          publicIPAddress: {
            id: vnet02BastionPip.id
          }
        }
      }
    ]
  }
}

// VNet02 Windows VM NIC
resource vnet02Vm01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = if (deployVMs) {
  name: 'VNet02-VM01-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: '${vnet02.id}/subnets/${vnet02TenantSubnetName}'
          }
        }
      }
    ]
  }
}

// VNet02 Windows 11 VM Instance (VMSS)
resource vnet02VM 'Microsoft.Compute/virtualMachines@2024-11-01' = if (deployVMs) {
  name: 'VNet02-VM01'
  location: location
  properties: {
    virtualMachineScaleSet: {
      id: vmss.id
    }
    hardwareProfile: {
      vmSize: 'Standard_B2s'
    }
    osProfile: {
      computerName: 'VNet02-VM01'
      adminUsername: adminUsername
      adminPassword: adminPassword
      windowsConfiguration: {
        enableAutomaticUpdates: true
        provisionVMAgent: true
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'MicrosoftWindowsDesktop'
        offer: 'windows-11'
        sku: 'win11-23h2-pro'  
        version: 'latest'
      }
      osDisk: {
        name: 'VNet02-VM01-OSDisk'
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'Standard_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: vnet02Vm01Nic.id
          properties: {
            primary: true
          }
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

// ============================================================================
// VNet03 - Spoke VNet with Ubuntu VM and Cisco CSR 1000v
// ============================================================================

resource vnet03 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: vnet03Name
  location: location
  properties: {
    addressSpace: {
      addressPrefixes: [
        vnet03AddressSpace
      ]
    }
    subnets: [
      {
        name: vnet03TenantSubnetName
        properties: {
          addressPrefix: vnet03TenantSubnetPrefix
        }
      }
      {
        name: vnet03VpnSubnetName
        properties: {
          addressPrefix: vnet03VpnSubnetPrefix
        }
      }
    ]
  }
}

// VNet03 Ubuntu VM NIC
resource vnet03Vm01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = if (deployVMs) {
  name: 'VNet03-VM01-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: '${vnet03.id}/subnets/${vnet03TenantSubnetName}'
          }
        }
      }
    ]
  }
}

// VNet03 Ubuntu VM Instance (VMSS)
resource vnet03VM 'Microsoft.Compute/virtualMachines@2024-11-01' = if (deployVMs) {
  name: 'VNet03-VM01'
  location: location
  properties: {
    virtualMachineScaleSet: {
      id: vmss.id
    }
    hardwareProfile: {
      vmSize: 'Standard_B1s'
    }
    osProfile: {
      computerName: 'VNet03-VM01'
      adminUsername: adminUsername
      adminPassword: adminPassword
      linuxConfiguration: {
        disablePasswordAuthentication: false
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        name: 'VNet03-VM01-OSDisk'
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'Standard_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: vnet03Vm01Nic.id
          properties: {
            primary: true
          }
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

// VNet03 Ubuntu VM02 NIC
resource vnet03Vm02Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = if (deployVMs) {
  name: 'VNet03-VM02-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: '${vnet03.id}/subnets/${vnet03TenantSubnetName}'
          }
        }
      }
    ]
  }
}

// VNet03 Ubuntu VM02 Instance (VMSS)
resource vnet03VM02 'Microsoft.Compute/virtualMachines@2024-11-01' = if (deployVMs) {
  name: 'VNet03-VM02'
  location: location
  properties: {
    virtualMachineScaleSet: {
      id: vmss.id
    }
    hardwareProfile: {
      vmSize: 'Standard_B1s'
    }
    osProfile: {
      computerName: 'VNet03-VM02'
      adminUsername: adminUsername
      adminPassword: adminPassword
      linuxConfiguration: {
        disablePasswordAuthentication: false
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        name: 'VNet03-VM02-OSDisk'
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'Standard_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: vnet03Vm02Nic.id
          properties: {
            primary: true
          }
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

// VNet03 Linux Router (Ubuntu with strongSwan) Public IP (required for S2S VPN)
resource vnet03RouterPip 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
  name: '${vnet03Name}-router01-pip'
  location: location
  sku: {
    name: 'Standard'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
  }
}

// VNet03 Route Table for Tenant Subnet (forward 10.1.0.0/24 to StrongSwan router)
resource vnet03TenantRouteTable 'Microsoft.Network/routeTables@2024-01-01' = {
  name: '${vnet03Name}-tenant-rt'
  location: location
  properties: {
    routes: [
      {
        name: 'ToVNet01ViaStrongSwan'
        properties: {
          addressPrefix: '10.1.0.0/24'
          nextHopType: 'VirtualAppliance'
          nextHopIpAddress: vnet03RouterStaticIp
        }
      }
    ]
  }
}

// VNet03 Router NIC (on VPN subnet with public IP and IP forwarding)
resource vnet03Router01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = if (deployVMs) {
  name: 'VNet03-router01-nic'
  location: location
  properties: {
    enableIPForwarding: true
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Static'
          privateIPAddress: vnet03RouterStaticIp
          subnet: {
            id: '${vnet03.id}/subnets/${vnet03VpnSubnetName}'
          }
          publicIPAddress: {
            id: vnet03RouterPip.id
          }
        }
      }
    ]
  }
}

// VNet03 strongSwan Router VM Instance (VMSS)
resource vnet03Router 'Microsoft.Compute/virtualMachines@2024-11-01' = if (deployVMs) {
  name: 'VNet03-router01'
  location: location
  properties: {
    virtualMachineScaleSet: {
      id: vmss.id
    }
    hardwareProfile: {
      vmSize: 'Standard_B2s'
    }
    osProfile: {
      computerName: 'VNet03-router01'
      adminUsername: adminUsername
      adminPassword: adminPassword
      linuxConfiguration: {
        disablePasswordAuthentication: false
      }
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
        name: 'VNet03-router01-OSDisk'
        createOption: 'FromImage'
        managedDisk: {
          storageAccountType: 'Standard_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: vnet03Router01Nic.id
          properties: {
            primary: true
          }
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
  dependsOn: [
    vnet01GwPipExisting
    secretValidationScript
  ]
}

// Update VNet03 tenant subnet to include route table (after NIC and route table are created)
resource vnet03TenantSubnetUpdate 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = if (deployVMs) {
  parent: vnet03
  name: vnet03TenantSubnetName
  properties: {
    addressPrefix: vnet03TenantSubnetPrefix
    routeTable: {
      id: vnet03TenantRouteTable.id
    }
  }
  dependsOn: [
    vnet03Router01Nic
  ]
}

// ============================================================================
// S2S VPN Connection - VNet01 Gateway to Cisco CSR in VNet03
// ============================================================================

// Local Network Gateway representing the Linux Router
resource localNetworkGateway 'Microsoft.Network/localNetworkGateways@2024-01-01' = if (deployVMs) {
  name: '${vnet01Name}-lng-vnet03-router'
  location: location
  properties: {
    gatewayIpAddress: vnet03RouterPip.properties.ipAddress
    localNetworkAddressSpace: {
      addressPrefixes: [
        vnet03AddressSpace
      ]
    }
  }
}

// S2S VPN Connection
resource s2sConnection 'Microsoft.Network/connections@2024-01-01' = if (deployVMs) {
  name: '${vnet01Name}-conn-vnet03-s2s'
  location: location
  properties: {
    connectionType: 'IPsec'
    virtualNetworkGateway1: {
      id: vpnGatewayId
      properties: {}
    }
    localNetworkGateway2: {
      id: localNetworkGateway.id
      properties: {}
    }
    sharedKey: vpnSharedKey
    connectionProtocol: 'IKEv2'
    enableBgp: false
  }
}

// Deployment script to configure strongSwan after all resources are created
resource strongswanConfigScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = if (deployVMs) {
  name: 'VNet03-router01-strongswan-config'
  location: location
  kind: 'AzureCLI'
  properties: {
    forceUpdateTag: '1.0.0'
    azCliVersion: '2.50.0'
    timeout: 'PT10M'
    retentionInterval: 'PT1H'
    environmentVariables: [
      {
        name: 'ROUTER_VM_NAME'
        value: vnet03Router.name
      }
      {
        name: 'RESOURCE_GROUP'
        value: resourceGroup().name
      }
      {
        name: 'ROUTER_PUBLIC_IP'
        value: vnet03RouterPip.properties.ipAddress
      }
      {
        name: 'GATEWAY_PUBLIC_IP' 
        value: gwPipIpAddress
      }
      {
        name: 'SHARED_KEY'
        secureValue: vpnSharedKey
      }
    ]
    scriptContent: '''
      echo "Configuring strongSwan on router VM..."
      
      # Wait for VM to be fully provisioned
      echo "Waiting for VM to be ready..."
      sleep 60
      
      # Configure strongSwan IPsec configuration
      az vm run-command invoke \
        --resource-group "$RESOURCE_GROUP" \
        --name "$ROUTER_VM_NAME" \
        --command-id RunShellScript \
        --scripts "
          # Install strongSwan if not already installed
          if ! command -v ipsec &> /dev/null; then
            echo 'Installing strongSwan...'
            export DEBIAN_FRONTEND=noninteractive
            apt-get update -y
            apt-get install -y strongswan strongswan-pki libcharon-extra-plugins
            echo 'net.ipv4.ip_forward = 1' >> /etc/sysctl.conf
            echo 'net.ipv4.conf.all.accept_redirects = 0' >> /etc/sysctl.conf
            echo 'net.ipv4.conf.all.send_redirects = 0' >> /etc/sysctl.conf
            sysctl -p
            systemctl enable strongswan-starter
          fi
          
          echo 'Configuring strongSwan with actual IP addresses and shared key...'
          
          # Configure ipsec.conf with actual IP addresses
          cat > /etc/ipsec.conf << EOF
config setup
    charondebug=\"ike 1, knl 1, cfg 0\"
    uniqueids=no

conn azure-vnet01
    authby=secret
    left=10.3.0.132
    leftsubnet=10.3.0.0/24
    leftid=$ROUTER_PUBLIC_IP
    right=$GATEWAY_PUBLIC_IP
    rightid=$GATEWAY_PUBLIC_IP
    rightsubnet=10.1.0.0/24
    ike=aes256-sha1-modp1024,aes128-sha1-modp1024!
    esp=aes256-sha1,aes128-sha1!
    keyingtries=0
    ikelifetime=3h
    lifetime=1h
    dpddelay=30
    dpdtimeout=120
    dpdaction=restart
    auto=start
EOF

          # Configure ipsec.secrets with actual shared key
          cat > /etc/ipsec.secrets << EOF
$ROUTER_PUBLIC_IP $GATEWAY_PUBLIC_IP : PSK \"$SHARED_KEY\"
EOF
          
          # Set proper permissions
          chmod 600 /etc/ipsec.secrets
          
          # Start strongSwan service
          systemctl start strongswan-starter
          systemctl status strongswan-starter
          
          # Wait a moment for service to fully start
          sleep 5
          
          # Manually initiate the connection
          ipsec up azure-vnet01
          
          # Check connection status
          ipsec statusall
          
          echo 'strongSwan configuration completed successfully!'
        "
      
      echo "strongSwan router configuration completed."
    '''
  }
  dependsOn: [
    vnet01VpnGatewayExisting
    s2sConnection
  ]
}

// ============================================================================
// Deployment Script Identity (for scripts that call Azure APIs)
// ============================================================================

resource deploymentScriptIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'vpnControl-mi'
  location: location
}

// ============================================================================
// Automation Account & Runbook
// ============================================================================

resource automationAccount 'Microsoft.Automation/automationAccounts@2023-11-01' = {
  name: 'vpnAutomation'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    sku: {
      name: 'Basic'
    }
  }
}

// Network Contributor on the resource group (for VPN connection management)
resource automationNetworkContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, automationAccount.id, '4d97b98b-1d4f-4787-a291-c67834d212e7')
  properties: {
    principalId: automationAccount.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4d97b98b-1d4f-4787-a291-c67834d212e7')
  }
}

// Key Vault Secrets User on the Key Vault (for reading VPN shared key)
resource automationKeyVaultSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, automationAccount.id, '4633458b-17de-408a-b874-0445c86b69e6')
  properties: {
    principalId: automationAccount.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

// Virtual Machine Contributor on the resource group (for running commands on router VM)
resource automationVmContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, automationAccount.id, '9980e02c-c2be-4d73-94e8-173b1dc7cf3c')
  properties: {
    principalId: automationAccount.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '9980e02c-c2be-4d73-94e8-173b1dc7cf3c')
  }
}

// Automation Contributor on the Automation Account (for deployment script to publish runbooks)
resource deployScriptAutomationContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: automationAccount
  name: guid(automationAccount.id, deploymentScriptIdentity.id, 'f353d9bd-d4a6-484e-a77a-8050b599b867')
  properties: {
    principalId: deploymentScriptIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'f353d9bd-d4a6-484e-a77a-8050b599b867')
  }
}

resource vpnControlRunbook 'Microsoft.Automation/automationAccounts/runbooks@2023-11-01' = {
  parent: automationAccount
  name: 'vpnControl'
  location: location
  properties: {
    runbookType: 'PowerShell'
    logProgress: true
    logVerbose: false
  }
}

resource publishRunbookScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'vpnControl-Publish'
  location: location
  kind: 'AzurePowerShell'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${deploymentScriptIdentity.id}': {}
    }
  }
  properties: {
    forceUpdateTag: '1.0.8'
    azPowerShellVersion: '11.0'
    timeout: 'PT10M'
    retentionInterval: 'PT1H'
    environmentVariables: [
      {
        name: 'SUBSCRIPTION_ID'
        value: subscription().subscriptionId
      }
      {
        name: 'RESOURCE_GROUP'
        value: resourceGroup().name
      }
      {
        name: 'AUTOMATION_ACCOUNT'
        value: automationAccount.name
      }
      {
        name: 'RUNBOOK_NAME'
        value: vpnControlRunbook.name
      }
    ]
    scriptContent: '''
      $scriptContent = @'
param(
    [Parameter(Mandatory)]
    [ValidateSet("On", "Off")]
    [string]$Action
)

$ErrorActionPreference = 'Stop'

# Static configuration
$ResourceGroupName = "VPN-jonor"
$ConnectionName    = "VNet01-conn-vnet03-s2s"
$KeyVaultName      = "jonor-2jv6ghsaeyzaq"
$VpnKeySecretName  = "vpn-shared-key"
$RouterVmName      = "VNet03-router01"

# Auth with Managed Identity (Azure China cloud)
Connect-AzAccount -Identity -Environment AzureChinaCloud -WarningAction SilentlyContinue | Out-Null

Write-Output "Action: $Action"
Write-Output "Connection: $ConnectionName"

switch ($Action) {

    "On" {
        Write-Output "Restoring shared key from Key Vault..."
        $sharedKey = Get-AzKeyVaultSecret -VaultName $KeyVaultName -Name $VpnKeySecretName -AsPlainText

        $null = Set-AzVirtualNetworkGatewayConnectionSharedKey `
            -ResourceGroupName $ResourceGroupName `
            -Name $ConnectionName `
            -Value $sharedKey `
            -Force

        Write-Output "Shared key restored. Restarting strongSwan on router..."
        $null = Invoke-AzVMRunCommand `
            -ResourceGroupName $ResourceGroupName `
            -VMName $RouterVmName `
            -CommandId 'RunShellScript' `
            -ScriptString 'ipsec restart'

        Write-Output "strongSwan restarted. VPN tunnel re-initiating."
    }

    "Off" {
        $disabledKey = "disabled-$(Get-Random -Minimum 100000 -Maximum 999999)"
        Write-Output "Changing shared key to invalidate tunnel..."

        Set-AzVirtualNetworkGatewayConnectionSharedKey `
            -ResourceGroupName $ResourceGroupName `
            -Name $ConnectionName `
            -Value $disabledKey `
            -Force

        Write-Output "Shared key changed. VPN tunnel will drop."
    }
}

Write-Output "Completed action '$Action' for '$ConnectionName'"
'@

      $tempFile = [System.IO.Path]::GetTempFileName() + ".ps1"
      Set-Content -Path $tempFile -Value $scriptContent

      # Upload runbook draft content via REST API (avoids Import-AzAutomationRunbook bug)
      $draftPath = "/subscriptions/$($env:SUBSCRIPTION_ID)/resourceGroups/$($env:RESOURCE_GROUP)/providers/Microsoft.Automation/automationAccounts/$($env:AUTOMATION_ACCOUNT)/runbooks/$($env:RUNBOOK_NAME)/draft/content?api-version=2023-11-01"
      $result = Invoke-AzRestMethod -Path $draftPath -Method PUT -Payload $scriptContent
      if ($result.StatusCode -notin 200,201,202) {
          Write-Error "Failed to upload runbook content: $($result.Content)"
          exit 1
      }
      Write-Output "Runbook draft uploaded successfully."

      # Publish the runbook via REST API
      $publishPath = "/subscriptions/$($env:SUBSCRIPTION_ID)/resourceGroups/$($env:RESOURCE_GROUP)/providers/Microsoft.Automation/automationAccounts/$($env:AUTOMATION_ACCOUNT)/runbooks/$($env:RUNBOOK_NAME)/publish?api-version=2023-11-01"
      $result = Invoke-AzRestMethod -Path $publishPath -Method POST
      if ($result.StatusCode -notin 200,202) {
          Write-Error "Failed to publish runbook: $($result.Content)"
          exit 1
      }

      Remove-Item $tempFile -Force
      Write-Output "Runbook '$($env:RUNBOOK_NAME)' published successfully."
    '''
  }
}

// Daily schedule: Connect VPN at 9:00 AM (China Standard Time)
resource vpnConnectSchedule 'Microsoft.Automation/automationAccounts/schedules@2023-11-01' = {
  parent: automationAccount
  name: 'vpn-connect-daily-9am'
  properties: {
    frequency: 'Day'
    interval: 1
    startTime: '2026-04-02T09:00:00+08:00'
    timeZone: 'China Standard Time'
    description: 'Daily VPN connect at 9:00 AM CST'
  }
}

// Daily schedule: Disconnect VPN at 5:00 PM (China Standard Time)
resource vpnDisconnectSchedule 'Microsoft.Automation/automationAccounts/schedules@2023-11-01' = {
  parent: automationAccount
  name: 'vpn-disconnect-daily-5pm'
  properties: {
    frequency: 'Day'
    interval: 1
    startTime: '2026-04-02T17:00:00+08:00'
    timeZone: 'China Standard Time'
    description: 'Daily VPN disconnect at 5:00 PM CST'
  }
}

// Link connect schedule to runbook with Action=On
resource vpnConnectJobSchedule 'Microsoft.Automation/automationAccounts/jobSchedules@2023-11-01' = {
  parent: automationAccount
  name: guid(automationAccount.id, vpnConnectSchedule.name, 'connect')
  properties: {
    schedule: {
      name: vpnConnectSchedule.name
    }
    runbook: {
      name: vpnControlRunbook.name
    }
    parameters: {
      Action: 'On'
    }
  }
  dependsOn: [
    publishRunbookScript
  ]
}

// Link disconnect schedule to runbook with Action=Off
resource vpnDisconnectJobSchedule 'Microsoft.Automation/automationAccounts/jobSchedules@2023-11-01' = {
  parent: automationAccount
  name: guid(automationAccount.id, vpnDisconnectSchedule.name, 'disconnect')
  properties: {
    schedule: {
      name: vpnDisconnectSchedule.name
    }
    runbook: {
      name: vpnControlRunbook.name
    }
    parameters: {
      Action: 'Off'
    }
  }
  dependsOn: [
    publishRunbookScript
  ]
}

// ============================================================================
// Outputs
// ============================================================================

@description('VNet01 VPN Gateway Public IP')
output vnet01VpnGatewayPublicIp string = gwPipIpAddress

@description('VNet03 Linux Router Public IP')
output vnet03RouterPublicIp string = vnet03RouterPip.properties.ipAddress

@description('VNet01 VPN Gateway Resource ID')
output vnet01VpnGatewayId string = vpnGatewayId

@description('P2S VPN Client Address Pool')
output p2sClientPool string = p2sClientAddressPool

@description('P2S NAT Pool (translated addresses)')
output p2sNatPoolOutput string = p2sNatPool

@description('VNet02 Bastion Host Name')
output vnet02BastionName string = vnet02Bastion.name

@description('Key Vault Name for storing secrets')
output keyVaultName string = keyVault.name

@description('Key Vault URI')
output keyVaultUri string = keyVault.properties.vaultUri
