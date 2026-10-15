# Changelog

All notable changes to SentinelWatcher are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.1.0] - 2026-10-15

First public release.

### Added

- Dashboard with analytics rules, log ingestion, data freshness and data collection rule status for a Microsoft Sentinel workspace.
- Analytics Rules page: incidents of the last 30 days per rule, and where every incident comes from (existing rule, deleted rule, or no analytics rule such as Microsoft Defender XDR incidents).
- Log Ingestion page: billable volume and estimated cost per table over 30 days, with estimated volumes and data lake tier tables labeled.
- Data Connectors page: data freshness per connector from the newest record in its tables, with configurable gap thresholds per connector type and a Quiet state for event-driven connectors.
- Collection Rules page: data collection rules in the workspace resource group and whether their destination tables receive data. Can be turned off with `Sentinel:EnableDcrPage`.
- Sentinel Health page: health score, rule and automation run failures, auto-disabled rules and connector health from `_SentinelHealth()`.
- Access for people without any Azure role: the backend reads Azure with a user-assigned managed identity, and access to the app is controlled by the Entra app role `SentinelWatcher.Viewer` with "Assignment required".
- Workspace allow-list (`Sentinel:Workspaces`): requests for any other workspace return 403.
- Bicep template and `azuredeploy.json` (Deploy to Azure): Linux App Service, managed identity, optional Application Insights, and least-privilege role assignments (Microsoft Sentinel Reader, plus a custom role with only `Microsoft.Insights/dataCollectionRules/read` for the DCR page).
- App registration script with the Viewer role, "Assignment required" and tenant-wide admin consent for the sign-in permissions.
- CI and release workflows, tests that fake Log Analytics and ARM.

### Security

- No endpoint accepts a query, ARM path or Resource Graph query. All queries are fixed in code; the only inputs are the allow-listed workspace and the Sentinel Health trend window.
- Raw Sentinel health descriptions and resource IDs of other workspaces are not sent to the browser.
- No client secret: the app registration has none, and the backend uses a managed identity.

### Known issues

- DCRs outside the workspace resource group are not shown.
- Custom detection rules created in the Microsoft Defender portal are not shown. The Microsoft Graph API for them is still in beta.
- Data lake tier tables are detected by the `Auxiliary` table plan, confirmed through the tables API but not yet in the Defender portal tier view. Their cost estimate uses the analytics tier price.
- See the README for the full list.

[Unreleased]: https://github.com/RycnCDL/SentinelWatcher/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/RycnCDL/SentinelWatcher/releases/tag/v0.1.0
