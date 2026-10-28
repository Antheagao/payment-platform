// Least-privilege data-plane roles for the sites' managed identities. SQL permissions are granted inside
// the database with T-SQL instead (infra/README.md), because Azure RBAC does not reach table rights.

param apiPrincipalId string
param workerPrincipalId string
param dashboardPrincipalId string
param keyVaultName string
param serviceBusNamespaceName string
param applicationInsightsName string

// Built-in role definition ids.
var keyVaultSecretsUser = '4633458b-17de-408a-b874-0445c86b69e6'
var serviceBusDataSender = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var serviceBusDataReceiver = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
var monitoringMetricsPublisher = '3913510d-42f4-4e42-8a64-420c390055eb'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource namespace 'Microsoft.ServiceBus/namespaces@2024-01-01' existing = {
  name: serviceBusNamespaceName
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: applicationInsightsName
}

// The API and the worker read the JWT signing key; the dashboard holds no backend secrets.
resource secretReaders 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in [apiPrincipalId, workerPrincipalId]: {
    name: guid(vault.id, principalId, keyVaultSecretsUser)
    scope: vault
    properties: {
      principalId: principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    }
  }
]

// Only the worker touches the queue: it relays the outbox and consumes the events.
resource queueRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for roleId in [serviceBusDataSender, serviceBusDataReceiver]: {
    name: guid(namespace.id, workerPrincipalId, roleId)
    scope: namespace
    properties: {
      principalId: workerPrincipalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
    }
  }
]

// Application Insights has local auth disabled, so every sender needs this role.
resource telemetryPublishers 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for principalId in [apiPrincipalId, workerPrincipalId, dashboardPrincipalId]: {
    name: guid(applicationInsights.id, principalId, monitoringMetricsPublisher)
    scope: applicationInsights
    properties: {
      principalId: principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisher)
    }
  }
]
