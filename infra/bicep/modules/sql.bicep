// Azure SQL Database: Microsoft Entra authentication only (no SQL logins exist), TLS 1.2 or later, no public
// network access, and the payments database on serverless General Purpose with geo-redundant backups.
// Database users for the apps' managed identities are created with T-SQL after deployment (infra/README.md).

param name string
param location string
param tags object
param administratorGroupName string
param administratorGroupObjectId string
param privateEndpointSubnetId string
param virtualNetworkId string

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: 'sql-${name}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: administratorGroupName
      sid: administratorGroupObjectId
      tenantId: tenant().tenantId
      principalType: 'Group'
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: 'payments'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Geo'
    zoneRedundant: false
  }
}

module privateEndpoint 'private-endpoint.bicep' = {
  name: 'private-endpoint-sql'
  params: {
    name: '${name}-sql'
    location: location
    tags: tags
    subnetId: privateEndpointSubnetId
    virtualNetworkId: virtualNetworkId
    targetResourceId: server.id
    groupId: 'sqlServer'
    privateDnsZoneName: 'privatelink${environment().suffixes.sqlServerHostname}'
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
