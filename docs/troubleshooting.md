---
title: Troubleshooting
description: Common sign-in, permission and data problems in SentinelWatcher and how to fix them.
version: 0.1.0
last_validated: 2026-10-15
---

# Troubleshooting

Most problems show up in the API log. In Azure:

```bash
az webapp log tail -g <app-rg> -n <appName>
```

With Application Insights enabled, the same messages are in its `traces` table.

## "Could not load data, see server log"

A page or Dashboard card shows this when the API could not read Azure for that section. It never shows zeros or "all good" in that case. The code says why; the server log has the details (search for "Section ... failed").

| Code | Meaning | Fix |
|------|---------|-----|
| `AuthenticationFailed` | No Azure token for the app's identity | In Azure: check `Sentinel__ManagedIdentityClientId` and that the identity is assigned to the web app. Locally: run `az login`. |
| `AccessDenied` | Azure returned 401 or 403 | Check the identity's role assignments ([Permissions](permissions.md)); new assignments take a few minutes |
| `NotFound` | An Azure resource was not found | Check the workspace resource ID in `Sentinel:Workspaces` |
| `Timeout` | Azure did not answer in time | Usually temporary |
| `AzureRequestFailed` | Any other Azure error | See the server log |

Failed sections are retried after 30 seconds (`CacheSettings:ErrorRetrySeconds`), so the page recovers by itself once the cause is fixed. If a refresh fails while older data exists, the older data is shown with a "latest refresh failed" note.

## Sign-in

| Symptom | Cause | Fix |
|---------|-------|-----|
| AADSTS50105: the signed in user is not assigned to a role | The user has no `SentinelWatcher.Viewer` assignment | Assign the role: Enterprise applications, SentinelWatcher, Users and groups |
| "Need admin approval" | User consent is turned off in the tenant and admin consent was not granted | App registrations, SentinelWatcher, API permissions, Grant admin consent (or rerun the setup script without `--skip-admin-consent` for a new registration) |
| AADSTS50011: redirect URI mismatch | The app URL is not a redirect URI of the app registration | App registrations, SentinelWatcher, Authentication, add `https://<appName>.azurewebsites.net` under Single-page application |
| "Access not assigned" page after sign-in | Signed in, but the token has no Viewer role | Assign the role, then sign out and in again |
| The page shows "AzureAd:TenantId and AzureAd:ClientId are not configured" | App settings missing | Set `AzureAd__TenantId` and `AzureAd__ClientId` |

## Data

| Symptom | Cause | Fix |
|---------|-------|-----|
| Pages stay empty or show "Data is still loading" for minutes | The managed identity has no access yet. The log shows `AuthorizationFailed` or 403 | Check the role assignments ([Installation](installation.md#check-the-permissions)), wait a few minutes, restart the app |
| 403 "This workspace is not configured in SentinelWatcher" | The requested workspace is not in `Sentinel:Workspaces` | Add it, see [Adding workspaces](configuration.md#adding-workspaces) |
| App does not start, log says "Sentinel:Workspaces contains an invalid resource ID" | A workspace entry is malformed or still a placeholder | Use the full resource ID of the workspace |
| Sentinel Health says health monitoring is not enabled | `_SentinelHealth()` has no data | Turn on health monitoring: Microsoft Sentinel, Settings, Auditing and health monitoring |
| Collection Rules page is missing | `Sentinel:EnableDcrPage` is `false` | Intended. Set it to `true` and grant DCR access, see [Permissions](permissions.md#dcr-access-options) |
| A DCR shows Error | None of its destination tables in this workspace received data in the last 24 hours | The log names the table: "DCR <name>: destination table <table> has no data in the last 24 hours" |
| A DCR is not listed | It is in another resource group than the workspace | Known issue in v0.1 |
| "Not available in data lake tier" | The table is in the data lake tier, which the Log Analytics query API can't read | Expected. Check those tables in the Defender portal. |
| Connector shows Quiet | Event-driven connector without recent events | Not an error. Change the behavior per connector type, see [Connector health](configuration.md#connector-health) |
| Connector on Sentinel Health shows Stale | No `SentinelHealth` event for it in the selected range. Sentinel logs connector events mainly on status changes and while failing, so a long-healthy connector can be stale. Old entries of removed or reconnected connectors are stale too. | Compare with the data freshness on the Data Connectors page |
| Incident numbers differ from Advanced Hunting | SentinelWatcher counts each incident once (latest row per `IncidentName`) by `CreatedTime` in the last 30 days. Counting `SecurityIncident` rows counts updates. | Use the same definition, see the [FAQ](faq.md#why-do-incident-numbers-differ-from-my-own-query) |
| Rule count is one higher than in the Defender portal | The Fusion rule. The Defender portal hides it in onboarded workspaces (Defender XDR correlation replaces it), the `alertRules` API still returns it. | Expected, see the [FAQ](faq.md#why-does-sentinelwatcher-show-one-more-rule-than-the-defender-portal) |
| Log ingestion cost differs from the bill | The estimate uses one price per GB (`CacheSettings:CostPerGBDollars`) | Set your effective price. Commitment tiers, benefits and data lake pricing are not considered. |

## Deployment

| Symptom | Fix |
|---------|-----|
| Role assignment fails with "AuthorizationFailed" | The deploying account needs Owner or User Access Administrator on the workspace resource group |
| "RoleDefinitionDoesNotExist" right after the custom role was created | Rerun the deployment; new role definitions can take a moment to replicate |
| Custom roles are not allowed in your organization | Deploy with `dcrRole=MonitoringReader` or `enableDcrPage=false` |
| `az webapp deploy` returns 401 | Update the Azure CLI. Basic-auth publishing is off, and the CLI must deploy with your Entra ID sign-in. |
