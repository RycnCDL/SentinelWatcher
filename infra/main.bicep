// SentinelWatcher: App Service (API and SPA on one origin) with a user-assigned managed identity
// and least-privilege access to one existing Microsoft Sentinel workspace.
//
// Deploy into the resource group that should hold the app. The workspace can be in another
// resource group or subscription. The deploying account needs:
// - Contributor on the app resource group
// - Owner or User Access Administrator on the workspace resource group (role assignments, and
//   the custom role definition when dcrAccess is CustomRole)
//
// The Entra app registration is created separately with scripts/setup-app-registration.sh.

targetScope = 'resourceGroup'

@description('Azure region for the app resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Web app name. It becomes https://<appName>.azurewebsites.net, which must be a redirect URI of the app registration.')
@minLength(2)
@maxLength(50) // leaves room for the prefixes of the plan (max 60) and workspace (max 63) names
param appName string = 'sentinelwatcher-${uniqueString(resourceGroup().id)}'

@description('App Service plan SKU (Linux).')
@allowed([
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
  'S3'
  'P0v3'
  'P1v3'
  'P2v3'
  'P3v3'
])
param appServicePlanSku string = 'B1'

@description('Directory (tenant) ID of the SentinelWatcher app registration. Defaults to the tenant of this deployment.')
param entraTenantId string = tenant().tenantId

@description('Application (client) ID of the SentinelWatcher app registration, printed by scripts/setup-app-registration.sh.')
param entraClientId string

@description('Resource ID of the Log Analytics workspace with Microsoft Sentinel: /subscriptions/{id}/resourceGroups/{rg}/providers/Microsoft.OperationalInsights/workspaces/{name}.')
param workspaceResourceId string

@description('Show the Data Collection Rules page. When false, no DCR role is assigned.')
param enableDcrPage bool = true

@description('How the identity reads data collection rules when the DCR page is on. CustomRole grants only Microsoft.Insights/dataCollectionRules/read. MonitoringReader is a fallback for environments where custom roles are not allowed; it can read all monitoring data and settings in the resource group.')
@allowed([
  'CustomRole'
  'MonitoringReader'
])
param dcrRole string = 'CustomRole'

@description('URL of the release package (zip) to run from. Leave empty to deploy a package yourself with az webapp deploy.')
param packageUrl string = 'https://github.com/RycnCDL/SentinelWatcher/releases/latest/download/sentinelwatcher.zip'

@description('Create Application Insights (with its own small Log Analytics workspace) for app telemetry.')
param enableApplicationInsights bool = true

@description('Tags applied to all resources created by this template.')
param tags object = {}

// /subscriptions/{sub}/resourceGroups/{rg}/providers/Microsoft.OperationalInsights/workspaces/{name}
var workspaceIdParts = split(workspaceResourceId, '/')
var workspaceSubscriptionId = workspaceIdParts[2]
var workspaceResourceGroup = workspaceIdParts[4]
var dcrAccess = enableDcrPage ? dcrRole : 'None'

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${appName}'
  location: location
  tags: tags
}

resource telemetryWorkspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = if (enableApplicationInsights) {
  name: 'log-${appName}'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = if (enableApplicationInsights) {
  name: 'appi-${appName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: telemetryWorkspace.id
  }
}

resource plan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: 'asp-${appName}'
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServicePlanSku
  }
  properties: {
    reserved: true
  }
}

var baseSettings = [
  {
    name: 'AzureAd__Instance'
    value: environment().authentication.loginEndpoint
  }
  {
    name: 'AzureAd__TenantId'
    value: entraTenantId
  }
  {
    name: 'AzureAd__ClientId'
    value: entraClientId
  }
  {
    name: 'Sentinel__ManagedIdentityClientId'
    value: identity.properties.clientId
  }
  {
    name: 'Sentinel__Workspaces__0'
    value: workspaceResourceId
  }
  {
    name: 'Sentinel__EnableDcrPage'
    value: string(enableDcrPage)
  }
]

var packageSettings = empty(packageUrl)
  ? []
  : [
      {
        name: 'WEBSITE_RUN_FROM_PACKAGE'
        value: packageUrl
      }
    ]

var telemetrySettings = enableApplicationInsights
  ? [
      {
        name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
        value: appInsights!.properties.ConnectionString
      }
    ]
  : []

resource site 'Microsoft.Web/sites@2024-04-01' = {
  name: appName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      appCommandLine: 'dotnet SentinelWatcher.Api.dll'
      alwaysOn: true
      http20Enabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      appSettings: concat(baseSettings, packageSettings, telemetrySettings)
    }
  }
}

// No FTP or basic-auth deployments. az webapp deploy uses Microsoft Entra ID instead.
resource ftpBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'ftp'
  properties: {
    allow: false
  }
}

resource scmBasicAuth 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-04-01' = {
  parent: site
  name: 'scm'
  properties: {
    allow: false
  }
}

module workspaceAccess 'modules/workspace-access.bicep' = {
  name: 'sentinelwatcher-workspace-access-${uniqueString(appName)}'
  scope: resourceGroup(workspaceSubscriptionId, workspaceResourceGroup)
  params: {
    principalId: identity.properties.principalId
    dcrAccess: dcrAccess
  }
}

@description('URL of the app. Add it as a SPA redirect URI of the app registration.')
output appUrl string = 'https://${site.properties.defaultHostName}'

@description('Name of the web app, for az webapp deploy and az webapp log tail.')
output webAppName string = site.name

@description('Client ID of the managed identity the app uses for all Azure calls.')
output managedIdentityClientId string = identity.properties.clientId

@description('Object ID of the managed identity. Use it to list its role assignments.')
output managedIdentityPrincipalId string = identity.properties.principalId

@description('Data collection rule access that was assigned: CustomRole, MonitoringReader or None.')
output dcrAccess string = dcrAccess
