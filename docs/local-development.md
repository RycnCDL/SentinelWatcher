---
title: Local development
description: Run SentinelWatcher on your machine against a real workspace with az login, and run the tests.
version: 0.1.0
last_validated: 2026-10-15
---

# Local development

Locally, the API calls Azure as you (`DefaultAzureCredential`, usually your `az login`) instead of a managed identity. Everything else works as in Azure.

## Requirements

- .NET 10 SDK
- Node.js 22
- Azure CLI
- Bash for the app registration script (Git Bash on Windows, or Azure Cloud Shell)
- An app registration with the Viewer role assigned to you: `bash scripts/setup-app-registration.sh --assign-me` (see [Installation](installation.md#step-2-create-the-app-registration)). It always adds `http://localhost:5173` as a redirect URI.

## Azure access for your account

Your account needs what the managed identity needs, on the workspace resource group: Microsoft Sentinel Reader, plus Monitoring Reader (or the custom DCR role) for the Collection Rules page. Owner, Contributor or Reader on the resource group work too.

```bash
rg=/subscriptions/<sub>/resourceGroups/<workspace-rg>
me=$(az ad signed-in-user show --query id -o tsv)
az role assignment create --assignee-object-id "$me" --assignee-principal-type User --role "Microsoft Sentinel Reader" --scope "$rg"
az role assignment create --assignee-object-id "$me" --assignee-principal-type User --role "Monitoring Reader" --scope "$rg"
```

## Run it

1. Sign in: `az login --tenant <tenant-id>`. If you are also signed in to Visual Studio with another account, `DefaultAzureCredential` may use that one first; sign out there.
2. Copy `src/api/appsettings.Development.sample.json` to `src/api/appsettings.Development.json` (gitignored) and fill in tenant ID, client ID and the workspace resource ID. Leave `ManagedIdentityClientId` empty.
3. Start the API on http://localhost:5000:

   ```bash
   cd src/api
   dotnet run
   ```

   The log should say "Using DefaultAzureCredential". http://localhost:5000/config.json shows your client and tenant ID.
4. In a second terminal, start the web app:

   ```bash
   cd src/client
   npm ci
   npm run dev
   ```

5. Open http://localhost:5173. The Vite dev server forwards `/api` and `/config.json` to the API.

## Tests

```bash
dotnet test SentinelWatcher.sln
```

The tests need no Azure access. They run the API in memory with fake Log Analytics and ARM clients (`tests/SentinelWatcher.Api.Tests/Fakes`), and cover the Viewer role, the workspace allow-list, input validation, caching, incident attribution, DCR status, connector thresholds, data lake tiers and the template's role logic.

Client checks:

```bash
cd src/client
npm run lint
npm run build
```

## Build the release package

```bash
pwsh scripts/build-package.ps1
```

This writes `artifacts/sentinelwatcher.zip`: the API with the web app in `wwwroot`, ready for App Service.
