// One virtual network: the App Service sites integrate into `app`, and every data service is reached
// through a private endpoint in `private-endpoints`.

param name string
param location string
param tags object

resource virtualNetwork 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${name}'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.20.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'app'
        properties: {
          addressPrefix: '10.20.1.0/24'
          delegations: [
            {
              name: 'app-service'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
        }
      }
      {
        name: 'private-endpoints'
        properties: {
          addressPrefix: '10.20.2.0/24'
        }
      }
    ]
  }

  resource appSubnet 'subnets' existing = {
    name: 'app'
  }

  resource privateEndpointSubnet 'subnets' existing = {
    name: 'private-endpoints'
  }
}

output virtualNetworkId string = virtualNetwork.id
output appSubnetId string = virtualNetwork::appSubnet.id
output privateEndpointSubnetId string = virtualNetwork::privateEndpointSubnet.id
