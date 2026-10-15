---
title: Permissions
description: Which Azure and Entra ID permissions SentinelWatcher, its viewers and the people who deploy it need, and why.
version: 0.1.0
last_validated: 2026-10-15
---

# Permissions

## Overview

| Who | Needs | Where |
|-----|-------|-------|
| Viewers | App role `SentinelWatcher.Viewer` | Entra ID enterprise application |
| Managed identity of the app | Microsoft Sentinel Reader, plus a custom role with `Microsoft.Insights/dataCollectionRules/read` for the Collection Rules page | Resource group of the workspace |
| Person who creates the app registration | Application Administrator or Cloud Application Administrator | Entra ID |
| Person who deploys the template | Contributor on the app resource group, and Owner or User Access Administrator on the workspace resource group | Azure |
| Developer running it locally | The same roles as the managed identity, for their own account | Resource group of the workspace |

Viewers need **no Azure role and no Sentinel role**. See [Security model](security-model.md) for why that is safe.

## Managed identity: every call and the permission it needs

The table lists every Azure call the API makes. Checked against the role definitions on Microsoft Learn.

| Call | API | Permission | Covered by |
|------|-----|------------|------------|
| `Usage` (ingestion volume, freshness of tables outside the fixed list) | Log Analytics query by workspace resource ID | `Microsoft.OperationalInsights/workspaces/query/read`, `workspaces/query/*/read` | Microsoft Sentinel Reader |
| `SecurityIncident` (incidents per rule) | Log Analytics query | same | Microsoft Sentinel Reader |
| `union isfuzzy=true` over a fixed table list (last log per table) | Log Analytics query | same | Microsoft Sentinel Reader |
| `_SentinelHealth()` (Sentinel Health page) | Log Analytics query | same | Microsoft Sentinel Reader |
| List analytics rules (`alertRules`) | ARM, `Microsoft.SecurityInsights` | `Microsoft.SecurityInsights/*/read` | Microsoft Sentinel Reader |
| List data connectors (`dataConnectors`) | ARM, `Microsoft.SecurityInsights` | `Microsoft.SecurityInsights/*/read` | Microsoft Sentinel Reader |
| Sentinel onboarding state (`onboardingStates/default`) | ARM, `Microsoft.SecurityInsights` | `Microsoft.SecurityInsights/*/read` | Microsoft Sentinel Reader |
| `SecurityInsights(<workspace>)` solution | ARM, `Microsoft.OperationsManagement/solutions` | `Microsoft.OperationsManagement/solutions/read` | Microsoft Sentinel Reader, **resource group scope** |
| Table plans (data lake tier detection) | ARM, `Microsoft.OperationalInsights/workspaces/tables` | `Microsoft.OperationalInsights/workspaces/tables/read` (part of `workspaces/*/read`) | Microsoft Sentinel Reader |
| List data collection rules in the workspace resource group | ARM, `Microsoft.Insights/dataCollectionRules` | `Microsoft.Insights/dataCollectionRules/read` | Custom role "SentinelWatcher DCR Reader", or Monitoring Reader as fallback |

**Log Analytics Reader is not needed.** Microsoft Sentinel Reader already allows queries on the workspace.

**The Collection Rules page needs nothing else.** It reads the DCR definitions from ARM (`dataCollectionRules/read`). Whether a destination table receives data comes from the Log Analytics queries above, which Microsoft Sentinel Reader covers.

### Why resource group scope

- The `SecurityInsights(<workspace>)` solution resource is a sibling of the workspace in the resource group, not a child of it. A role on the workspace alone can't read it.
- Data collection rules are separate resources in the resource group.

Everything else in that resource group becomes readable for the identity, so keep the Sentinel workspace in a **dedicated resource group**. See [What the managed identity can technically read](security-model.md#what-the-managed-identity-can-technically-read), including the actions Microsoft Sentinel Reader allows beyond reading.

### DCR access options

Template parameters `enableDcrPage` and `dcrRole`:

| Setting | Role assigned for DCRs |
|---------|------------------------|
| `enableDcrPage=true`, `dcrRole=CustomRole` (default) | Custom role "SentinelWatcher DCR Reader \<hash\>" with only `Microsoft.Insights/dataCollectionRules/read`, assignable only in the workspace resource group |
| `enableDcrPage=true`, `dcrRole=MonitoringReader` | Monitoring Reader. Use it only where custom roles are not allowed: it can read all monitoring data and settings in the resource group (`*/read`). |
| `enableDcrPage=false` | None. The page is hidden and `/api/dcr` returns 404. |

The template does not remove role assignments. If you switch options on an existing deployment, remove the old DCR role assignment yourself (see [Installation](installation.md#change-or-remove-permissions)).

## Viewers

The app registration defines one app role, `SentinelWatcher.Viewer`, with "Assignment required" turned on for the enterprise application. Assign it to users or groups: Entra admin center, Enterprise applications, SentinelWatcher, Users and groups, Add user/group.

The API checks for the role on every endpoint. Without it, Entra ID refuses the sign-in (AADSTS50105), and a token without the role gets 403 from the API.

## Sign-in permissions (consent)

The web app requests the SentinelWatcher API scope (`api://<client-id>/access_as_user`) and the standard sign-in permissions `openid`, `profile` and `offline_access` from Microsoft Graph.

- The app is pre-authorized for its own API scope, so nobody is asked to consent to it.
- `scripts/setup-app-registration.sh` declares the three Graph permissions and grants **tenant-wide admin consent** for them. This is needed in tenants that turn off user consent, where users would otherwise see "Need admin approval". Granting consent for delegated permissions needs Application Administrator or Cloud Application Administrator. With `--skip-admin-consent`, the script leaves it to you: App registrations, SentinelWatcher, API permissions, Grant admin consent.

## People who deploy SentinelWatcher

| Task | Role |
|------|------|
| Create the app registration, grant admin consent for sign-in, assign the Viewer role | Application Administrator or Cloud Application Administrator |
| Deploy the template into the app resource group | Contributor on that resource group |
| Create the custom role and the role assignments in the workspace resource group | Owner or User Access Administrator on that resource group. Role Based Access Control Administrator can assign roles but can't create the custom role; use `dcrRole=MonitoringReader` or `enableDcrPage=false` in that case. |

## Local development

Locally, the API uses `DefaultAzureCredential`, usually your `az login`. Your account then needs what the managed identity needs: Microsoft Sentinel Reader on the workspace resource group, plus Monitoring Reader or the custom role if you want to see the Collection Rules page. Owner, Contributor or Reader on the resource group also work. See [Local development](local-development.md).
