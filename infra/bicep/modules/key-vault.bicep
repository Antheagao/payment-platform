// Key Vault for application secrets, such as the JWT signing key (ADR 004). Access is by Azure RBAC only,
// deletes are recoverable and purge-protected, and the vault has no public network access.

param name string
param location string
param tags object
param privateEndpointSubnetId string
param virtualNetworkId string

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${name}'
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    publicNetworkAccess: 'Disabled'
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
    }
  }
}

module privateEndpoint 'private-endpoint.bicep' = {
  name: 'private-endpoint-key-vault'
  params: {
    name: '${name}-kv'
    location: location
    tags: tags
    subnetId: privateEndpointSubnetId
    virtualNetworkId: virtualNetworkId
    targetResourceId: vault.id
    groupId: 'vault'
    privateDnsZoneName: 'privatelink.vaultcore.azure.net'
  }
}

output name string = vault.name
