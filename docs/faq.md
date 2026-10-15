---
title: FAQ
description: Frequently asked questions about SentinelWatcher, its data, permissions, costs and the Microsoft Defender portal.
version: 0.1.0
last_validated: 2026-10-15
---

# FAQ

## Do viewers need a Sentinel role or PIM?

No. They need the Entra app role `SentinelWatcher.Viewer` only. The app reads Azure with its own managed identity. See [Security model](security-model.md).

## Does SentinelWatcher change anything in my workspace?

No. It only reads. Note that the Microsoft Sentinel Reader role it gets technically allows a few actions beyond reading (classic alert rules, deployments, support tickets), listed in the [security model](security-model.md#what-the-managed-identity-can-technically-read). SentinelWatcher does not use them.

## Does it work with the Microsoft Defender portal?

Yes. SentinelWatcher only uses Azure Resource Manager and the Log Analytics query API, so it works the same for workspaces managed in the Azure portal and workspaces onboarded to the Defender portal. It does not depend on the Azure portal experience for Sentinel, which Microsoft is retiring.

Two Defender portal features are not covered in v0.1:

- **Custom detection rules** created in the Defender portal (Advanced Hunting) are not part of the Sentinel `alertRules` API, so they are not listed. Their incidents carry no Sentinel rule ID, so they usually appear under "Defender XDR / no analytics rule". Reading them needs the Microsoft Graph security API (`/security/rules/detectionRules`), which is in beta as of October 2026. Planned for v0.2.
- **Data lake tier tables** can't be read through the Log Analytics query API. They show as "Not available in data lake tier". SentinelWatcher detects them by the `Auxiliary` table plan in the Log Analytics tables API; this was confirmed through the API, not yet against the tier view in the Defender portal.

## Why do incident numbers differ from my own query?

SentinelWatcher counts **incidents created in the last 30 days**, each once, using the latest row per `IncidentName` in `SecurityIncident`. Counting rows instead counts every update of an incident. To compare in Advanced Hunting or Log Analytics:

```kql
SecurityIncident
| where TimeGenerated > ago(30d)
| summarize arg_max(TimeGenerated, *) by IncidentName
| where CreatedTime > ago(30d)
| count
```

On the Analytics Rules page, every incident is assigned to an existing rule, a rule that no longer exists ("Rule deleted or not found"), or no analytics rule (for example "Defender XDR / no analytics rule"), so the numbers add up to the total. An incident that references several rules counts once per rule in that table.

## What is the difference between "Data freshness" and "Connector health"?

**Data freshness** (Dashboard, Data Connectors page) checks whether data arrives: the newest record in each connector's tables. **Connector health** (Sentinel Health page) is what Microsoft Sentinel reports about its connectors in the `SentinelHealth` table. They can disagree: a connector can report success while its source sends nothing, or data can arrive while the health event is old.

## What does Quiet mean?

The connector only sends data when something happens, for example Azure Activity, and nothing happened recently. It is not an error. The connector types and thresholds are configurable, see [Connector health](configuration.md#connector-health).

## Can one instance show several workspaces?

Yes. Add every workspace resource ID to `Sentinel:Workspaces` and grant the managed identity the same roles on each workspace resource group. All viewers see all listed workspaces. See [Adding workspaces](configuration.md#adding-workspaces).

## Where is data stored?

Only in the app's memory, for 10 minutes per refresh. There is no database and no storage account. Logs go to the App Service log stream and, if enabled, Application Insights.

## What does it cost?

The template creates an App Service plan (B1 by default), Application Insights with a small Log Analytics workspace (can be turned off), and a managed identity (free). The queries run against the Sentinel workspace; queries on analytics tier data are included in its price. See the Azure pricing pages for current prices in your region.

## How accurate is the ingestion cost?

It is an estimate: billable GB from the `Usage` table times one price per GB (`CacheSettings:CostPerGBDollars`, default 2.76 USD). It does not consider commitment tiers, free data benefits, or data lake tier pricing. Tables without Usage data are estimated from their record count and marked "estimated".

## Can I run it somewhere other than Azure App Service?

v0.1 is built and tested for App Service with a user-assigned managed identity. The API uses `DefaultAzureCredential` when no managed identity is configured, but other hosting is not documented or tested.
