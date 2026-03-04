// ============================================================================
// Multi-VNet Infrastructure Deployment for Azure China (China North 3)
// ============================================================================
// VNet01: Hub VNet with VPN Gateway, Ubuntu VM
// VNet02: Spoke VNet with Windows 11 VM and Bastion
// VNet03: Spoke VNet with Ubuntu VM and Cisco CSR 1000v
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
param aadTenantId string

@description('Entra ID Audience (Application ID) for P2S VPN - Use Azure VPN Enterprise App ID')
param aadAudience string = 'c632b3df-fb67-4d84-bdcf-b95ad541b5c8'

@description('Key Vault name for storing secrets')
param keyVaultName string = 'kv-vpngw-${uniqueString(resourceGroup().id)}'

@description('Admin password secret name in Key Vault')
param adminPasswordSecretName string = 'vm-admin-password'

@description('VPN shared key secret name in Key Vault')
param vpnSharedKeySecretName string = 'vpn-shared-key'

@description('Deploy VMs (set to false for initial Key Vault setup)')
param deployVMs bool = false

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
    enableRbacAuthorization: false
    accessPolicies: []
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Deployment script to validate Key Vault secrets exist
resource secretValidationScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = if (deployVMs) {
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
      echo "Validating Key Vault secrets..."
      
      # Check if admin password secret exists
      if ! az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$ADMIN_SECRET_NAME" --query "id" -o tsv > /dev/null 2>&1; then
        echo "ERROR: Secret '$ADMIN_SECRET_NAME' not found in Key Vault '$KEY_VAULT_NAME'"
        echo "Please add the secret using: az keyvault secret set --vault-name $KEY_VAULT_NAME --name $ADMIN_SECRET_NAME --value 'YourPassword'"
        exit 1
      fi
      
      # Check if VPN shared key secret exists  
      if ! az keyvault secret show --vault-name "$KEY_VAULT_NAME" --name "$VPN_SECRET_NAME" --query "id" -o tsv > /dev/null 2>&1; then
        echo "ERROR: Secret '$VPN_SECRET_NAME' not found in Key Vault '$KEY_VAULT_NAME'"
        echo "Please add the secret using: az keyvault secret set --vault-name $KEY_VAULT_NAME --name $VPN_SECRET_NAME --value 'YourSharedKey'"
        exit 1
      fi
      
      echo "All required secrets found in Key Vault!"
    '''
  }
  dependsOn: [
    keyVault
  ]
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
resource vnet01GwPip 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
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

// VNet01 VPN Gateway
resource vnet01VpnGateway 'Microsoft.Network/virtualNetworkGateways@2024-01-01' = {
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
    capacity: 4
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
    platformFaultDomainCount: 1
    constrainedMaximumCapacity: false
  }
}

// VNet01 Ubuntu VM NIC
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
      adminPassword: keyVault.getSecret(adminPasswordSecretName)
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
      adminPassword: keyVault.getSecret(adminPasswordSecretName)
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
          routeTable: {
            id: vnet03TenantRouteTable.id
          }
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
      adminPassword: keyVault.getSecret(adminPasswordSecretName)
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
          nextHopIpAddress: vnet03Router01Nic.properties.ipConfigurations[0].properties.privateIPAddress
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
          privateIPAllocationMethod: 'Dynamic'
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
      adminPassword: keyVault.getSecret(adminPasswordSecretName)
      linuxConfiguration: {
        disablePasswordAuthentication: false
      }
      customData: base64('''#!/bin/bash
# Install strongSwan for IPsec VPN
apt-get update
apt-get install -y strongswan strongswan-pki libcharon-extra-plugins

# Enable IP forwarding
echo "net.ipv4.ip_forward = 1" >> /etc/sysctl.conf
echo "net.ipv4.conf.all.accept_redirects = 0" >> /etc/sysctl.conf
echo "net.ipv4.conf.all.send_redirects = 0" >> /etc/sysctl.conf
sysctl -p

# Configure strongSwan for S2S VPN to VNet01
cat > /etc/ipsec.conf << EOF
config setup
    charondebug="ike 1, knl 1, cfg 0"
    uniqueids=no

conn azure-vnet01
    authby=secret
    left=%defaultroute
    leftsubnet=10.3.0.0/24
    leftid=${vnet03RouterPip.properties.ipAddress}
    right=${vnet01GwPip.properties.ipAddress}
    rightsubnet=10.1.0.0/24
    ike=aes256-sha256-modp1024!
    esp=aes256-sha256!
    keyingtries=0
    ikelifetime=1h
    lifetime=8h
    dpddelay=30
    dpdtimeout=120
    dpdaction=restart
    auto=start
EOF

# Configure strongSwan secrets with actual shared key
cat > /etc/ipsec.secrets << EOF
${vnet03RouterPip.properties.ipAddress} ${vnet01GwPip.properties.ipAddress} : PSK "${keyVault.getSecret(vpnSharedKeySecretName)}"
EOF

# Start strongSwan service
systemctl enable strongswan
systemctl start strongswan

echo "strongSwan fully configured and started with actual IP addresses and shared key."
''')
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: '0001-com-ubuntu-server-jammy'
        sku: '22_04-lts-gen2'
        version: 'latest'
      }
      osDisk: {
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
    vnet01GwPip
    vnet03RouterPip
    secretValidationScript
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
  dependsOn: [
    vnet03Router01
  ]
}

// S2S VPN Connection
resource s2sConnection 'Microsoft.Network/connections@2024-01-01' = if (deployVMs) {
  name: '${vnet01Name}-conn-vnet03-s2s'
  location: location
  properties: {
    connectionType: 'IPsec'
    virtualNetworkGateway1: {
      id: vnet01VpnGateway.id
      properties: {}
    }
    localNetworkGateway2: {
      id: localNetworkGateway.id
      properties: {}
    }
    sharedKey: keyVault.getSecret(vpnSharedKeySecretName)
    connectionProtocol: 'IKEv2'
    enableBgp: false
  }
}

// ============================================================================
// Outputs
// ============================================================================

@description('VNet01 VPN Gateway Public IP')
output vnet01VpnGatewayPublicIp string = vnet01GwPip.properties.ipAddress

@description('VNet03 Linux Router Public IP')
output vnet03RouterPublicIp string = vnet03RouterPip.properties.ipAddress

@description('VNet01 VPN Gateway Resource ID')
output vnet01VpnGatewayId string = vnet01VpnGateway.id

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
