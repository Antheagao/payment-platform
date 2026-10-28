// Log Analytics and workspace-based Application Insights. Local auth is off, so telemetry is accepted only
// from identities holding Monitoring Metrics Publisher (granted in role-assignments.bicep); the apps send
// it through the Azure Monitor OpenTelemetry distro (ADR 005).

param name string
param location string
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${name}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${name}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    DisableLocalAuth: true
  }
}

output applicationInsightsName string = applicationInsights.name
output connectionString string = applicationInsights.properties.ConnectionString
