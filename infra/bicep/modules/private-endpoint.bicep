// A private endpoint for one service, with its private DNS zone linked to the virtual network so the
// service's public name resolves to the private address from inside it.

param name string
param location string
param tags object
param subnetId string
param virtualNetworkId string

@description('The resource the endpoint connects to.')
param targetResourceId string

@description('The sub-resource, such as sqlServer, vault, or namespace.')
param groupId string

@description('The privatelink DNS zone for that service, such as privatelink.database.windows.net.')
param privateDnsZoneName string

resource zone 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: privateDnsZoneName
  location: 'global'
  tags: tags

  resource link 'virtualNetworkLinks' = {
    name: 'link-${name}'
    location: 'global'
    tags: tags
    properties: {
      registrationEnabled: false
      virtualNetwork: {
        id: virtualNetworkId
      }
    }
  }
}

resource endpoint 'Microsoft.Network/privateEndpoints@2024-05-01' = {
  name: 'pe-${name}'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: subnetId
    }
    privateLinkServiceConnections: [
      {
        name: 'pe-${name}'
        properties: {
          privateLinkServiceId: targetResourceId
          groupIds: [
            groupId
          ]
        }
      }
    ]
  }

  resource dns 'privateDnsZoneGroups' = {
    name: 'default'
    properties: {
      privateDnsZoneConfigs: [
        {
          name: replace(privateDnsZoneName, '.', '-')
          properties: {
            privateDnsZoneId: zone.id
          }
        }
      ]
    }
  }
}
