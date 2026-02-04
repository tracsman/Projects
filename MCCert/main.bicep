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

@description('Admin password for all VMs')
@secure()
param adminPassword string

@description('Entra ID Tenant ID for P2S VPN authentication')
param aadTenantId string

@description('Entra ID Audience (Application ID) for P2S VPN - Use Azure VPN Enterprise App ID')
param aadAudience string = 'c632b3df-fb67-4d84-bdcf-b95ad541b5c8'

@description('Pre-shared key for S2S VPN connection with Cisco router')
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
var vnet03TenantSubnetPrefix = '10.3.0.0/24'

// P2S VPN Configuration
var p2sClientAddressPool = '172.16.0.0/24'
var p2sNatPool = '192.168.10.0/24'

// Entra ID Issuer URL (China cloud uses different endpoint)
var aadIssuer = 'https://sts.chinacloudapi.cn/${aadTenantId}/'

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

// VNet01 Ubuntu VM NIC
resource vnet01Vm01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = {
  name: '${vnet01Name}-VM01-nic'
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

// VNet01 Ubuntu 24.04 LTS VM
resource vnet01Vm01 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: '${vnet01Name}-VM01'
  location: location
  properties: {
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
        name: '${vnet01Name}-VM01-osdisk'
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

// VNet02 Windows 11 VM NIC
resource vnet02Vm01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = {
  name: '${vnet02Name}-VM01-nic'
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

// VNet02 Windows 11 VM
resource vnet02Vm01 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: '${vnet02Name}-VM01'
  location: location
  properties: {
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
        name: '${vnet02Name}-VM01-osdisk'
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
    ]
  }
}

// VNet03 Ubuntu VM NIC
resource vnet03Vm01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = {
  name: '${vnet03Name}-VM01-nic'
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

// VNet03 Ubuntu VM
resource vnet03Vm01 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: '${vnet03Name}-VM01'
  location: location
  properties: {
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
        name: '${vnet03Name}-VM01-osdisk'
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

// VNet03 Linux Router NIC
resource vnet03Router01Nic 'Microsoft.Network/networkInterfaces@2024-01-01' = {
  name: '${vnet03Name}-router01-nic'
  location: location
  properties: {
    enableIPForwarding: true
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: '${vnet03.id}/subnets/${vnet03TenantSubnetName}'
          }
          publicIPAddress: {
            id: vnet03RouterPip.id
          }
        }
      }
    ]
  }
}

// VNet03 Linux Router (Ubuntu with strongSwan for S2S VPN)
resource vnet03Router01 'Microsoft.Compute/virtualMachines@2024-03-01' = {
  name: '${vnet03Name}-router01'
  location: location
  properties: {
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
      customData: base64('''#!/bin/bash
# Install strongSwan for IPsec VPN
apt-get update
apt-get install -y strongswan strongswan-pki libcharon-extra-plugins

# Enable IP forwarding
echo "net.ipv4.ip_forward = 1" >> /etc/sysctl.conf
echo "net.ipv4.conf.all.accept_redirects = 0" >> /etc/sysctl.conf
echo "net.ipv4.conf.all.send_redirects = 0" >> /etc/sysctl.conf
sysctl -p

# strongSwan config will need to be completed manually with VPN Gateway details
echo "strongSwan installed. Configure /etc/ipsec.conf and /etc/ipsec.secrets with VPN Gateway details."
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
        name: '${vnet03Name}-router01-osdisk'
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
// S2S VPN Connection - VNet01 Gateway to Cisco CSR in VNet03
// ============================================================================

// Local Network Gateway representing the Linux Router
resource localNetworkGateway 'Microsoft.Network/localNetworkGateways@2024-01-01' = {
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
resource s2sConnection 'Microsoft.Network/connections@2024-01-01' = {
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
    sharedKey: vpnSharedKey
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
