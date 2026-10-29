// One Linux App Service plan hosting the three sites. Each site has a system-assigned managed identity,
// integrates into the `app` subnet with all outbound traffic routed through it, accepts HTTPS only, and is
// probed on /health/ready, so an instance whose database or schema is not ready leaves rotation (ADR 005).
// The worker takes no inbound traffic at all.

param name string
param location string
param tags object
param appSubnetId string
param applicationInsightsConnectionString string
param keyVaultName string
param sqlServerFqdn string
param sqlDatabaseName string
param serviceBusFqdn string

// Managed identity through Microsoft.Data.SqlClient; there is no password to store or rotate.
var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Default;Encrypt=True;TrustServerCertificate=False'

var sharedSiteConfig = {
  alwaysOn: true
  ftpsState: 'Disabled'
  http20Enabled: true
  minTlsVersion: '1.2'
  scmMinTlsVersion: '1.2'
  healthCheckPath: '/health/ready'
  vnetRouteAllEnabled: true
}

var backendSettings = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'ConnectionStrings__Payments'
    value: sqlConnectionString
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: applicationInsightsConnectionString
  }
  {
    name: 'ServiceBus__FullyQualifiedNamespace'
    value: serviceBusFqdn
  }
  {
    // A Key Vault reference: App Service resolves it with the site's identity; the value never sits here.
    name: 'Jwt__SigningKey'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=jwt-signing-key)'
  }
]

// The key with which the dashboard names each person's address on sign-in (ADR 010); the API and the dashboard
// read the same secret, the worker never needs it.
var dashboardKeySetting = '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=dashboard-key)'

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${name}'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: 'P0v3'
    capacity: 2
  }
  properties: {
    reserved: true
  }
}

resource api 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-${name}-api'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    virtualNetworkSubnetId: appSubnetId
    siteConfig: union(sharedSiteConfig, {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appSettings: concat(backendSettings, [
        {
          name: 'Dashboard__Key'
          value: dashboardKeySetting
        }
      ])
    })
  }
}

resource worker 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-${name}-worker'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Disabled'
    virtualNetworkSubnetId: appSubnetId
    siteConfig: union(sharedSiteConfig, {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appSettings: backendSettings
    })
  }
}

resource dashboard 'Microsoft.Web/sites@2024-04-01' = {
  name: 'app-${name}-dashboard'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    virtualNetworkSubnetId: appSubnetId
    siteConfig: union(sharedSiteConfig, {
      linuxFxVersion: 'NODE|22-lts'
      appCommandLine: 'node server.js'
      healthCheckPath: '/'
      appSettings: [
        {
          name: 'API_ORIGIN'
          value: 'https://${api.properties.defaultHostName}'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: applicationInsightsConnectionString
        }
        {
          name: 'DASHBOARD_KEY'
          value: dashboardKeySetting
        }
        {
          // App Service's front end is the one proxy ahead of the site; 2 once Front Door is in front of it.
          name: 'TRUSTED_PROXY_HOPS'
          value: '1'
        }
      ]
    })
  }
}

output apiSiteName string = api.name
output workerSiteName string = worker.name
output apiHostName string = api.properties.defaultHostName
output dashboardHostName string = dashboard.properties.defaultHostName
output apiPrincipalId string = api.identity.principalId
output workerPrincipalId string = worker.identity.principalId
output dashboardPrincipalId string = dashboard.identity.principalId
