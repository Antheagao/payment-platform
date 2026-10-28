// Service Bus for the production outbox relay (ADR 003): the worker publishes outbox rows to the queue and
// consumers read from it. Shared-key auth is disabled, so only managed identities with a data role connect.
// Duplicate detection on the message id makes a relay that publishes the same outbox row twice harmless.

param name string
param location string
param tags object

@allowed([
  'Standard'
  'Premium'
])
param sku string
param privateEndpointSubnetId string
param virtualNetworkId string

var premium = sku == 'Premium'

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' = {
  name: 'sb-${name}'
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
    capacity: premium ? 1 : null
  }
  properties: {
    disableLocalAuth: true
    minimumTlsVersion: '1.2'
    publicNetworkAccess: premium ? 'Disabled' : 'Enabled'
  }

  resource paymentEvents 'queues' = {
    name: 'payment-events'
    properties: {
      lockDuration: 'PT1M'
      maxDeliveryCount: 10
      deadLetteringOnMessageExpiration: true
      defaultMessageTimeToLive: 'P14D'
      requiresDuplicateDetection: true
      duplicateDetectionHistoryTimeWindow: 'PT10M'
    }
  }
}

// Only Premium namespaces support private endpoints.
module privateEndpoint 'private-endpoint.bicep' = if (premium) {
  name: 'private-endpoint-service-bus'
  params: {
    name: '${name}-sb'
    location: location
    tags: tags
    subnetId: privateEndpointSubnetId
    virtualNetworkId: virtualNetworkId
    targetResourceId: namespace.id
    groupId: 'namespace'
    privateDnsZoneName: 'privatelink.servicebus.windows.net'
  }
}

output namespaceName string = namespace.name
// serviceBusEndpoint is https://<name>.servicebus.windows.net:443/ in the public cloud; keep only the host.
output fqdn string = split(replace(namespace.properties.serviceBusEndpoint, 'https://', ''), ':')[0]
