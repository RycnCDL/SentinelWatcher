---
title: Configuration
description: Every setting of SentinelWatcher, the Bicep template parameters, and how to set them in App Service or locally.
version: 0.1.0
last_validated: 2026-10-15
---

# Configuration

The API reads standard ASP.NET Core configuration: `appsettings.json`, then environment variables. In App Service, settings are app settings, with `__` (two underscores) instead of `:`, for example `Sentinel__EnableDcrPage`. Lists use an index: `Sentinel__Workspaces__0`, `Sentinel__Workspaces__1`.

The template sets everything that is required. You only need this page to change defaults or to add workspaces.

## Settings

### Sign-in (`AzureAd`)

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `AzureAd:TenantId` | Yes | | Directory (tenant) ID of the app registration |
| `AzureAd:ClientId` | Yes | | Application (client) ID of the app registration |
| `AzureAd:Instance` | No | `https://login.microsoftonline.com/` | Entra ID sign-in endpoint |

If tenant or client ID is missing, `/config.json` returns 503 and the web app shows the error.

### Workspaces and identity (`Sentinel`)

| Setting | Required | Default | Description |
|---------|----------|---------|-------------|
| `Sentinel:Workspaces` | Yes | empty | Allow-list of Log Analytics workspace resource IDs: `/subscriptions/{id}/resourceGroups/{rg}/providers/Microsoft.OperationalInsights/workspaces/{name}`. The only workspaces the app reads. Invalid entries stop the app at startup. |
| `Sentinel:ManagedIdentityClientId` | In Azure | empty | Client ID of the user-assigned managed identity. When empty, the API uses developer sign-in (Azure CLI first, then Visual Studio, VS Code and Azure PowerShell; never managed identity). |
| `Sentinel:EnableDcrPage` | No | `true` | Shows the Collection Rules page. When `false`, the page is hidden, `/api/dcr` returns 404, and no data collection rule is read. |

### Connector health

Settings for the Data Connectors (data freshness) status. A connector's gap is the time since the newest record in any of its tables. Last log times cover the last 24 hours, so thresholds above 1440 minutes have no effect.

| Setting | Default | Description |
|---------|---------|-------------|
| `ConnectorHealth:WarningAfterMinutes` | `60` | Gap after which a connector is Warning |
| `ConnectorHealth:ErrorAfterMinutes` | `120` | Gap after which a connector is Error. Never below the warning value. |
| `ConnectorHealth:Connectors:{type}:WarningAfterMinutes` | | Override for one connector type |
| `ConnectorHealth:Connectors:{type}:ErrorAfterMinutes` | | Override for one connector type |
| `ConnectorHealth:Connectors:{type}:EventDriven` | | `true`: the connector only sends data when something happens. A longer gap makes it **Quiet** instead of Warning or Error. |

`{type}` is the connector type shown on the Data Connectors page, for example `Syslog` or `AzureActivity` (not case-sensitive). Overrides are merged with these built-in defaults:

| Connector type | Warning | Error | Event-driven |
|----------------|---------|-------|--------------|
| `AzureActivity`, `ThreatIntelligence`, `ThreatIntelligenceTaxii`, `MicrosoftThreatIntelligence`, `AzureSecurityCenter`, `AzureADIdentityProtection`, `MicrosoftCloudAppSecurity`, `OfficeIRM`, `Sentinel` | 1440 | 1440 | yes |
| `AzureActiveDirectory` | 120 | 360 | no |
| `Office365` | 240 | 720 | no |
| all others | 60 | 120 | no |

Example in App Service: `ConnectorHealth__Connectors__Syslog__WarningAfterMinutes = 240`.

### Cache and cost (`CacheSettings`)

| Setting | Default | Description |
|---------|---------|-------------|
| `CacheSettings:HeavyQueryRefreshMinutes` | `10` | Age after which cached workspace data is refreshed in the background |
| `CacheSettings:EvictAfterMinutes` | `30` | Cached data of a workspace nobody requested for this long is removed |
| `CacheSettings:ErrorRetrySeconds` | `30` | A section that failed to load is retried after this many seconds instead of `HeavyQueryRefreshMinutes` |
| `CacheSettings:CostPerGBDollars` | `2.76` | Price per GB used for the cost estimate on Log Ingestion. Set it to your effective analytics tier price. Commitment tiers, benefits and data lake pricing are not considered. |

### Other

| Setting | Description |
|---------|-------------|
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Turns on telemetry to Application Insights. Set by the template when `enableApplicationInsights` is true. |
| `WEBSITE_RUN_FROM_PACKAGE` | App Service setting: URL of the release package the app runs from. Set by the template from `packageUrl`. |
| `Logging:LogLevel:*` | Standard ASP.NET Core log levels. Azure SDK categories (`Azure.Core`, `Azure.Identity`) are at Warning by default. |

## Template parameters

`infra/main.bicep` (and `infra/azuredeploy.json`, compiled from it):

| Parameter | Default | Description |
|-----------|---------|-------------|
| `entraClientId` | required | Application (client) ID of the app registration |
| `workspaceResourceId` | required | Resource ID of the Sentinel workspace. Can be in another resource group or subscription. |
| `appName` | `sentinelwatcher-<hash>` | Web app name, 2 to 50 characters. The app URL is `https://<appName>.azurewebsites.net`, which must be a redirect URI of the app registration. |
| `entraTenantId` | tenant of the deployment | Tenant of the app registration |
| `location` | resource group location | Region of the app resources |
| `appServicePlanSku` | `B1` | Linux App Service plan SKU: B1 to B3, S1 to S3, P0v3 to P3v3 |
| `enableDcrPage` | `true` | Collection Rules page. `false` assigns no DCR role. |
| `dcrRole` | `CustomRole` | `CustomRole` (only `dataCollectionRules/read`) or `MonitoringReader` (fallback where custom roles are not allowed) |
| `packageUrl` | latest GitHub release | Package to run. Pin a version, for example `https://github.com/RycnCDL/SentinelWatcher/releases/download/v0.1.0/sentinelwatcher.zip`, so a restart does not pick up a newer release. Empty: deploy the package yourself with `az webapp deploy`. |
| `enableApplicationInsights` | `true` | Application Insights with its own Log Analytics workspace (30 days retention) |
| `tags` | `{}` | Tags on all created resources |

Outputs: `appUrl`, `webAppName`, `managedIdentityClientId`, `managedIdentityPrincipalId`, `dcrAccess`.

## Adding workspaces

The template configures one workspace. To add another one:

1. Grant the managed identity the same roles on the other workspace's resource group (see [Permissions](permissions.md)). For example:

   ```bash
   az role assignment create --assignee-object-id <managedIdentityPrincipalId> --assignee-principal-type ServicePrincipal \
     --role "Microsoft Sentinel Reader" --scope /subscriptions/<sub>/resourceGroups/<other-workspace-rg>
   ```

2. Add the workspace resource ID as the next app setting, for example `Sentinel__Workspaces__1`, and restart the app.

The Collection Rules page of the new workspace also needs DCR read access on its resource group (custom role or Monitoring Reader).

## Local settings

For local development, copy `src/api/appsettings.Development.sample.json` to `appsettings.Development.json` (gitignored) and fill in the values. See [Local development](local-development.md).

The Vite dev server proxies `/api` and `/config.json` to `http://localhost:5000`. To use another address, set `SW_API_URL` in `src/client/.env` (see `.env.example`).
