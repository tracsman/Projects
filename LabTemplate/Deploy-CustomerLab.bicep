// Vnt: 10.17|10.x.0/24
// Pri: 192.168.x.16/30
// Sec: 192.168.x.20/30
// VLAN: xx0

param RGName string = resourceGroup().name
param CustomerNumber string = substring(RGName, length(RGName) - 2, 2)
param Location string = 'WestUS2'
param PeeringLocation string = 'Seattle'
param SecondOctet string = Location == 'WestUS2' ? '17' : '10'
param PeerASN string = Location == 'WestUS2' ? '65020' : '65021'
@secure()
param adminPwd string
param adminUser string = 'PathLabUser'

//
// Base Lab Build Out
//

// VNet
resource VNet01 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: '${RGName}-VNet01'
  location: Location
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.${SecondOctet}.${CustomerNumber}.0/24'
      ]
    }
    subnets: [
      {
        name: 'Tenant'
        properties: {
          addressPrefix: '10.${SecondOctet}.${CustomerNumber}.0/25'
        }
      }
      {
        name: 'GatewaySubnet'
        properties: {
          addressPrefix: '10.${SecondOctet}.${CustomerNumber}.128/25'
        }
      }
    ]
  }
}

// VM NIC
resource VMnic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: '${RGName}-VM01-nic'
  location: Location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: resourceId('Microsoft.Network/virtualNetworks/subnets', VNet01.name, 'Tenant')
          }
        }
      }
    ]
  }
}

// VM
resource VM01 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: '${RGName}-VM01'
  location: Location
  properties: {
    hardwareProfile: {
      vmSize: 'Standard_B2s'
    }
    osProfile: {
      computerName: '${RGName}-VM01'
      adminUsername: adminUser
      adminPassword: adminPwd
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        name: '${RGName}-VM01-disk'
        caching: 'ReadWrite'
        createOption: 'FromImage'
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: VMnic.id
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

// Gateway Public IP
resource VNetGwIp 'Microsoft.Network/publicIPAddresses@2024-05-01' = {
  name: '${VNet01.name}-gw-er-pip'
  location: Location
  properties: {
    publicIPAllocationMethod: 'static'
  }
  sku: {
    name: 'Standard'
    tier: 'Regional'
  }
}

// VNet Gateway
resource VNetErGw 'Microsoft.Network/virtualNetworkGateways@2024-05-01' = {
  name: '${VNet01.name}-gw-er'
  location: Location
  properties: {
    gatewayType: 'ExpressRoute'
    vpnType: 'RouteBased'
    sku: {
      name: 'Standard'
      tier: 'Standard'
    }
    ipConfigurations: [
      {
        name: 'ipconfig'
        properties: {
          publicIPAddress: {
            id: VNetGwIp.id
          }
          subnet: {
            id: resourceId('Microsoft.Network/virtualNetworks/subnets', VNet01.name, 'GatewaySubnet')
          }
        }
      }
    ]
  }
}

// ExpressRoute Circuit
resource ER 'Microsoft.Network/expressRouteCircuits@2024-05-01' = {
  name: '${RGName}-ER'
  location: Location
  sku:{
    family: 'MeteredData'
    tier: 'Standard'
    name: 'Standard_MeteredData'
  }
  properties:{
    serviceProviderProperties:{
      bandwidthInMbps: 50
        peeringLocation: PeeringLocation
      serviceProviderName: 'Equinix'
    }
    allowClassicOperations: false
  }
}

// ExpressRoute Private Peering
resource ERPvtPeering 'Microsoft.Network/expressRouteCircuits/peerings@2024-05-01' = {
  parent: ER
  name: 'AzurePrivatePeering'
  properties:{
    peerASN: int(PeerASN)
    peeringType: 'AzurePrivatePeering'
    primaryPeerAddressPrefix: '192.168.${CustomerNumber}.16/30'
    secondaryPeerAddressPrefix: '192.168.${CustomerNumber}.20/30'
    vlanId: int('${CustomerNumber}0')
  }
}
