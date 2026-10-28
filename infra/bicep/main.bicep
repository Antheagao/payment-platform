// The platform's Azure shape (docs/architecture.md, "Running in Azure"). Validated in CI, never deployed:
// this is the answer to "how would it run in production", in the form the hiring team's tooling reads.
//
// Every service authenticates with managed identities: no SQL logins, no Service Bus keys, no
// Application Insights instrumentation keys. Data services are reachable only through private endpoints.

targetScope = 'resourceGroup'

@description('Short environment name used in every resource name, such as dev or prod.')
@minLength(2)
@maxLength(6)
param environmentName string

@description('Region for every resource.')
param location string = resourceGroup().location

@description('Display name of the Microsoft Entra group that administers the SQL server.')
param sqlAdministratorGroupName string

@description('Object id of that Microsoft Entra group.')
param sqlAdministratorGroupObjectId string

@description('Premium gives Service Bus private networking; Standard is cheaper and public.')
@allowed([
  'Standard'
  'Premium'
])
param serviceBusSku string = 'Premium'

var name = 'pp-${environmentName}-${substring(uniqueString(resourceGroup().id), 0, 6)}'
var tags = {
  application: 'payment-platform'
  environment: environmentName
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    name: name
    location: location
    tags: tags
  }
}

module network 'modules/network.bicep' = {
  name: 'network'
  params: {
    name: name
    location: location
    tags: tags
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    name: name
    location: location
    tags: tags
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    virtualNetworkId: network.outputs.virtualNetworkId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    name: name
    location: location
    tags: tags
    administratorGroupName: sqlAdministratorGroupName
    administratorGroupObjectId: sqlAdministratorGroupObjectId
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    virtualNetworkId: network.outputs.virtualNetworkId
  }
}

module serviceBus 'modules/service-bus.bicep' = {
  name: 'service-bus'
  params: {
    name: name
    location: location
    tags: tags
    sku: serviceBusSku
    privateEndpointSubnetId: network.outputs.privateEndpointSubnetId
    virtualNetworkId: network.outputs.virtualNetworkId
  }
}

module apps 'modules/app-service.bicep' = {
  name: 'app-service'
  params: {
    name: name
    location: location
    tags: tags
    appSubnetId: network.outputs.appSubnetId
    applicationInsightsConnectionString: monitoring.outputs.connectionString
    keyVaultName: keyVault.outputs.name
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    serviceBusFqdn: serviceBus.outputs.fqdn
  }
}

module access 'modules/role-assignments.bicep' = {
  name: 'role-assignments'
  params: {
    apiPrincipalId: apps.outputs.apiPrincipalId
    workerPrincipalId: apps.outputs.workerPrincipalId
    dashboardPrincipalId: apps.outputs.dashboardPrincipalId
    keyVaultName: keyVault.outputs.name
    serviceBusNamespaceName: serviceBus.outputs.namespaceName
    applicationInsightsName: monitoring.outputs.applicationInsightsName
  }
}

output apiUrl string = 'https://${apps.outputs.apiHostName}'
output dashboardUrl string = 'https://${apps.outputs.dashboardHostName}'
output sqlServerFqdn string = sql.outputs.serverFqdn

// The managed identities' names, for infra/sql/grant-app-identities.sql.
output apiSiteName string = apps.outputs.apiSiteName
output workerSiteName string = apps.outputs.workerSiteName
