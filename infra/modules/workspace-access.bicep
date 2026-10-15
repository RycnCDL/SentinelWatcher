// Role assignments for the SentinelWatcher managed identity in the resource group that
// contains the Microsoft Sentinel workspace. Deployed by main.bicep with this resource group
// as scope, which can be in another subscription than the app.

targetScope = 'resourceGroup'

@description('Principal (object) ID of the user-assigned managed identity.')
param principalId string

@description('How the identity reads data collection rules: CustomRole (only Microsoft.Insights/dataCollectionRules/read), MonitoringReader (built-in fallback), or None (DCR page turned off).')
@allowed([
  'CustomRole'
  'MonitoringReader'
  'None'
])
param dcrAccess string

// Built-in role definition IDs (identical in every tenant).
var sentinelReaderRoleId = '8d289c81-5878-46d4-8554-54e1e3d8b5cb'
var monitoringReaderRoleId = '43d0d8ad-25c7-4714-9337-8ba259a9fe05'

// Microsoft Sentinel Reader at resource group scope covers:
// - Log Analytics queries by workspace resource ID (Usage, SecurityIncident, table union, _SentinelHealth())
// - Microsoft.SecurityInsights alertRules, dataConnectors and onboardingStates
// - Microsoft.OperationalInsights/workspaces/tables/read (table tiers)
// - Microsoft.OperationsManagement/solutions/read for the SecurityInsights(workspace) solution, which
//   is a sibling of the workspace, not a child. That is why the scope is the resource group.
resource sentinelReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, principalId, sentinelReaderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', sentinelReaderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'SentinelWatcher: read Microsoft Sentinel configuration and query the workspace'
  }
}

// Custom role with the single permission the DCR page needs. Role names are unique per tenant,
// so the name includes a hash of this resource group.
var dcrReaderRoleGuid = guid(resourceGroup().id, 'SentinelWatcher DCR Reader')

resource dcrReaderRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = if (dcrAccess == 'CustomRole') {
  name: dcrReaderRoleGuid
  properties: {
    roleName: 'SentinelWatcher DCR Reader ${uniqueString(resourceGroup().id)}'
    description: 'Read data collection rules. Used by SentinelWatcher for its Data Collection Rules page.'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.Insights/dataCollectionRules/read'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
    assignableScopes: [
      resourceGroup().id
    ]
  }
}

// Role definitions live at subscription level even when deployed to a resource group, and
// role assignments need that form of the ID.
var dcrRoleDefinitionId = subscriptionResourceId(
  'Microsoft.Authorization/roleDefinitions',
  dcrAccess == 'CustomRole' ? dcrReaderRoleGuid : monitoringReaderRoleId
)

// The access mode is part of the name, so switching modes creates a new assignment instead of
// failing on an update of the existing one. Remove the old assignment by hand after switching.
resource dcrReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (dcrAccess != 'None') {
  name: guid(resourceGroup().id, principalId, 'SentinelWatcher DCR access', dcrAccess)
  properties: {
    roleDefinitionId: dcrRoleDefinitionId
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'SentinelWatcher: read data collection rules for the Data Collection Rules page'
  }
  dependsOn: [
    dcrReaderRole
  ]
}

output dcrRoleDefinitionId string = dcrAccess == 'None' ? '' : dcrRoleDefinitionId
