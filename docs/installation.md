---
title: Installation
description: Deploy SentinelWatcher to Azure App Service with the Deploy to Azure button or the Azure CLI, and give people access.
version: 0.1.0
last_validated: 2026-10-15
---

# Installation

Plan about 15 minutes. You create an app registration, deploy the template, and assign the Viewer role.

## Prerequisites

- A Log Analytics workspace with Microsoft Sentinel, managed in the Azure portal or onboarded to the Microsoft Defender portal.
- Its resource ID: workspace in the Azure portal, Properties, Resource ID, or:

  ```bash
  az monitor log-analytics workspace show -g <workspace-rg> -n <workspace-name> --query id -o tsv
  ```

- Roles (details in [Permissions](permissions.md)):
  - Entra ID: Application Administrator or Cloud Application Administrator
  - Azure: Contributor on the resource group for the app, and Owner or User Access Administrator on the workspace resource group
- [Azure Cloud Shell](https://shell.azure.com) (Bash), or locally the Azure CLI and Bash (Linux, macOS, Git Bash on Windows).
- Optional, for Sentinel Health: health monitoring turned on for the workspace (Microsoft Sentinel, Settings, Auditing and health monitoring).

## Step 1: choose the app name

The app runs at `https://<appName>.azurewebsites.net`. Sign-in only works from URLs registered in the app registration, so choose the name first. It must be globally unique, 2 to 50 characters, letters, digits and hyphens, for example `sentinelwatcher-contoso`.

## Step 2: create the app registration

In Cloud Shell (Bash), clone the repository and run the setup script with your app URL:

```bash
git clone https://github.com/RycnCDL/SentinelWatcher.git
cd SentinelWatcher
bash scripts/setup-app-registration.sh --redirect-uri https://<appName>.azurewebsites.net --assign-me
```

The script:

- creates the app registration "SentinelWatcher" (single tenant, no client secret)
- adds the redirect URIs (your app URL and `http://localhost:5173` for development)
- defines the app role `SentinelWatcher.Viewer` and turns on "Assignment required"
- grants admin consent for the sign-in permissions (openid, profile, offline_access)
- with `--assign-me`, assigns the Viewer role to you

Note the **Client ID** it prints. Options: `--name` for another display name (the script stops if the name exists), `--skip-admin-consent` to grant consent yourself.

## Step 3: deploy

### Option A: Deploy to Azure button

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2FRycnCDL%2FSentinelWatcher%2Fmain%2Finfra%2Fazuredeploy.json)

Select or create a resource group for the app, then fill in:

- **Entra Client Id:** the client ID from step 2
- **Workspace Resource Id:** the resource ID of your Sentinel workspace
- **App Name:** the name from step 1

Leave the other parameters at their defaults unless you need something else (see [Configuration](configuration.md#template-parameters)).

### Option B: Azure CLI

```bash
az group create -n rg-sentinelwatcher -l westeurope

az deployment group create -g rg-sentinelwatcher -n sentinelwatcher \
  --template-file infra/main.bicep \
  --parameters appName=<appName> entraClientId=<client-id> \
               workspaceResourceId=<workspace-resource-id> \
               packageUrl=https://github.com/RycnCDL/SentinelWatcher/releases/download/v0.1.0/sentinelwatcher.zip
```

In PowerShell, replace the line continuation `\` with a backtick.

The deployment creates the managed identity, the App Service plan and web app, Application Insights, and the role assignments in the workspace resource group.

## Step 4: open the app

Wait about five minutes: new role assignments take a while to apply. Then restart the app once, so it does not keep results it read before the roles were active:

```bash
az webapp restart -g rg-sentinelwatcher -n <appName>
```

Open `https://<appName>.azurewebsites.net` and sign in. The first load of a workspace can take up to a minute.

## Step 5: give people access

Entra admin center, Enterprise applications, SentinelWatcher, Users and groups, Add user/group, role **SentinelWatcher Viewer**. Groups are easier to maintain than single users. Viewers need nothing else.

## Check the permissions

List what the managed identity can do. You should see only Microsoft Sentinel Reader and "SentinelWatcher DCR Reader ..." on the workspace resource group:

```bash
principalId=$(az deployment group show -g rg-sentinelwatcher -n sentinelwatcher \
  --query properties.outputs.managedIdentityPrincipalId.value -o tsv)
az role assignment list --assignee "$principalId" --all --query "[].{role:roleDefinitionName, scope:scope}" -o table
```

## Change or remove permissions

The template adds role assignments but never removes them. After switching `dcrRole` or turning the DCR page off, remove the DCR role assignment that is no longer needed:

```bash
az role assignment list --assignee "$principalId" --all \
  --query "[?roleDefinitionName!='Microsoft Sentinel Reader'].id" -o tsv | xargs -r -n1 az role assignment delete --ids
```

## Update to a new version

With a pinned `packageUrl`, set it to the new release and restart:

```bash
az webapp config appsettings set -g rg-sentinelwatcher -n <appName> \
  --settings WEBSITE_RUN_FROM_PACKAGE=https://github.com/RycnCDL/SentinelWatcher/releases/download/<version>/sentinelwatcher.zip
az webapp restart -g rg-sentinelwatcher -n <appName>
```

With the default `packageUrl` (latest release), the app picks up the newest release when it restarts. Read the [changelog](../CHANGELOG.md) first.

## Deploy your own build

Build the package with `pwsh scripts/build-package.ps1` (PowerShell 7, .NET 10 SDK, Node.js 22), deploy the template with `packageUrl=` (empty), then:

```bash
az webapp deploy -g rg-sentinelwatcher -n <appName> --src-path artifacts/sentinelwatcher.zip --type zip
```

Basic-auth publishing is turned off; the Azure CLI deploys with your Entra ID sign-in.

## Uninstall

Remove the role assignments and the custom role before deleting the identity, otherwise orphaned assignments stay behind:

```bash
az role assignment list --assignee "$principalId" --all --query "[].id" -o tsv | xargs -r -n1 az role assignment delete --ids
az role definition list --custom-role-only true --query "[?starts_with(roleName,'SentinelWatcher')].name" -o tsv \
  | xargs -r -I{} az role definition delete --name {} --scope /subscriptions/<sub>/resourceGroups/<workspace-rg>
az group delete -n rg-sentinelwatcher --yes
```

Then delete the app registration: Entra admin center, App registrations, SentinelWatcher, Delete.
