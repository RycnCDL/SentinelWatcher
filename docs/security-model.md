---
title: Security model
description: Who can see what in SentinelWatcher, why viewers need no Azure role, what the managed identity can read, and how to restrict access.
version: 0.1.0
last_validated: 2026-10-15
---

# Security model

SentinelWatcher lets people see Microsoft Sentinel health and usage without giving them an Azure role or a PIM activation. That only works safely if the app itself decides what they can see. This page explains how.

## Summary

- **Viewers** sign in with Microsoft Entra ID. They need the app role `SentinelWatcher.Viewer` and nothing else: no Azure role, no Sentinel role.
- **The backend** reads Azure with its own user-assigned managed identity. Every viewer sees data read by that identity.
- **Every query is fixed in code.** The browser can only choose a workspace from a configured allow-list and, on one page, a trend window of 1 to 30 days.
- **The browser gets summaries**, not log records: counts, names of rules, tables and connectors, statuses and fixed categories.

## Who can see what

| Who | Can do | Controlled by |
|-----|--------|---------------|
| User without the Viewer role | Nothing. Entra ID refuses the sign-in (AADSTS50105) because "Assignment required" is on. | Enterprise application, Users and groups |
| User with the Viewer role | See the dashboards of every workspace on the allow-list. | App role assignment and `Sentinel:Workspaces` |
| Anyone, signed in or not | Load the static web app and `/config.json` (client ID, tenant ID, scope, which pages are on). These values are public by design. | Not restricted |
| Administrator of the app resource group | Change app settings, for example the allow-list. | Azure RBAC on the app resource group |

There are no finer levels: every viewer sees the same data for every allowed workspace. If two groups must see different workspaces, deploy two instances, each with its own allow-list and app registration.

## Why viewers need no Azure role

The API validates the viewer's Entra ID access token (audience `api://<client-id>`, issued by your tenant) and requires the `SentinelWatcher.Viewer` role in its `roles` claim on every `/api` endpoint, including endpoints added later (it is the fallback authorization policy). The token is used for nothing else: it is never sent to Azure.

All Azure calls use the app's managed identity. Its permissions are fixed at deployment (see [Permissions](permissions.md)) and do not depend on who is signed in.

## Why fixed queries make this safe

Giving many people indirect access to one identity is only safe if they cannot steer what that identity does. SentinelWatcher accepts exactly two inputs from the browser:

| Input | Endpoints | Validation |
|-------|-----------|------------|
| `workspace` | all `/api` endpoints except `/api/workspaces` | Must match an entry of `Sentinel:Workspaces` exactly (case-insensitive). Missing or longer than 512 characters: 400. Not on the list: 403. The matched value from configuration is used, never the request value. |
| `days` | `/api/sentinelhealth` | Integer from 1 to 30, otherwise 400. Passed as the query time range, not inserted into the query text. |

Everything else is fixed in code:

- **Log Analytics queries** (KQL) are constants. The only text inserted into a query is a fixed list of table names that is part of the code.
- **ARM requests** use paths built from the configured workspace resource ID plus fixed segments, and only follow paging links that point to `https://management.azure.com/`.
- **Azure Resource Graph** is not used.

A test (`EndpointInputSurfaceTests`) fails when an endpoint gets any other parameter, so a new input cannot be added without a review.

## What the managed identity can technically read

The template assigns these roles on the **resource group that contains the workspace**:

| Role | Why |
|------|-----|
| Microsoft Sentinel Reader | Log Analytics queries, Sentinel configuration, table plans, the SecurityInsights solution |
| SentinelWatcher DCR Reader (custom, only `Microsoft.Insights/dataCollectionRules/read`) | Collection Rules page. Not assigned when the page is turned off. |

Be aware of what Microsoft Sentinel Reader allows beyond what SentinelWatcher uses. The identity can technically:

- **query every table of every Log Analytics workspace in that resource group**, including sign-in logs, security events and incidents. Confidential watchlists are excluded by the role. SentinelWatcher only runs its fixed queries, but anyone who controls the app's code or configuration controls this access.
- read all Microsoft Sentinel configuration (`Microsoft.SecurityInsights/*/read`), saved searches, query packs, workbooks, data sources and linked services
- read role definitions and role assignments (`Microsoft.Authorization/*/read`)
- create and manage classic metric alert rules (`Microsoft.Insights/alertRules/*`)
- create and manage ARM deployments in the resource group (`Microsoft.Resources/deployments/*`). A deployment can only create what the identity itself is allowed to create.
- create and update Azure support tickets (`Microsoft.Support/*`)

The role definition is published in the [Azure built-in roles reference](https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/security#microsoft-sentinel-reader). A custom role with only the actions SentinelWatcher needs is on the [roadmap](../README.md#roadmap) for v0.2.

**Recommendation:** keep the Sentinel workspace in a **dedicated resource group** without other workspaces or unrelated resources. Resource group scope is required (the SecurityInsights solution and the data collection rules are siblings of the workspace, not children), so everything else in that resource group is readable too.

The identity is not used for anything else. Protect the app resource group like the workspace: whoever can change the app's code or settings can use the identity.

## What reaches the browser

| Endpoint | Fields |
|----------|--------|
| `/config.json` | client ID, tenant ID, authority, API scope, `features.dcrPage` |
| `/api/workspaces` | resource ID, name, subscription ID and resource group of each allowed workspace, whether Sentinel is on |
| `/api/analyticsrules` | rule resource ID, rule display name, severity, enabled, incident count and last incident time; incident counts per source; for rules that no longer exist: the rule ID and the title of one of its incidents as a hint |
| `/api/ingestion` | table name, volume, estimated cost, estimated flag, tier |
| `/api/dataconnectors` | connector ID and display name, type, status, last log time, gap, a message generated by the app, table names |
| `/api/dcr` | DCR resource ID, name, location, data source names, destination table names, "this workspace" or "other workspace", tier, receiving status and last log time |
| `/api/sentinelhealth` | health score and counts; names and resource IDs of auto-disabled rules; connector name, kind, status, last event, stale flag and a fixed failure category; recent failures with time, resource type, name, kind, status and category |

When a section can't be loaded, the API returns an error code and a fixed message instead (for example `AuthenticationFailed`, "Could not get an Azure token for the app's identity. See the server log."). Exception details stay in the server log.

No endpoint returns log records, incident details, entities, user names, host names or IP addresses. Two kinds of names are chosen by your administrators and can contain anything they put there: **analytics rule names** and **DCR data source names**. The **title of one incident per deleted rule** can contain entity values if the rule put them into its alert titles.

Raw text that often contains identifiers stays on the server:

- **Sentinel health descriptions** are mapped to fixed categories such as "Authentication or permissions". The raw text is written to the server log once per resource and text.
- **Resource IDs of other workspaces** that a DCR sends data to are reduced to "Other workspace".

## Server logs and telemetry

The API logs to the App Service log stream and, when enabled, to Application Insights. Logs contain workspace names, table names, DCR and connector names, raw Sentinel health descriptions, and error messages from Azure. Request telemetry contains URLs, which include the workspace resource ID. Query results are not logged. Restrict access to the Application Insights resource and the App Service logs accordingly.

## Caching

Data is cached in memory per workspace for 10 minutes (`CacheSettings:HeavyQueryRefreshMinutes`) and shared by all viewers. That is intentional: everyone sees the data of the same identity. Nothing is written to disk or to a database. A restart clears the cache.

## How to restrict access

1. **Assign the Viewer role only to the people or groups who need it:** Entra admin center, Enterprise applications, SentinelWatcher, Users and groups. Keep "Assignment required" on.
2. **Apply Conditional Access** to the SentinelWatcher enterprise application like to any other app (MFA, compliant device, locations).
3. **Limit the workspaces** with `Sentinel:Workspaces`. Only listed workspaces can be read, even if the identity has access to more.
4. **Turn off the Collection Rules page** (`Sentinel:EnableDcrPage=false`, template parameter `enableDcrPage=false`) if you do not want to grant any access to data collection rules.
5. **Keep the workspace in a dedicated resource group**, see above.
6. **Restrict the app resource group**: whoever can change its settings or deploy code controls the identity.
7. **Remove access** by removing the role assignment of the user or group. A signed-in user keeps access until the current access token expires (60 to 90 minutes by default).
