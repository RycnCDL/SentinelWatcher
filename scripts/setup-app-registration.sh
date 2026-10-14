#!/bin/bash
# ==============================================================================
# SentinelWatcher - Azure AD App Registration Setup Script
# ==============================================================================
# This script creates the App Registration required for SentinelWatcher.
# Run this with: bash scripts/setup-app-registration.sh
#
# Prerequisites:
#   - Azure CLI installed and logged in (az login)
#   - Global Administrator or Application Administrator role
# ==============================================================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

echo -e "${GREEN}=== SentinelWatcher - App Registration Setup ===${NC}"
echo ""

# Check az cli
if ! command -v az &> /dev/null; then
    echo -e "${RED}Error: Azure CLI (az) is not installed.${NC}"
    echo "Install it from: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
fi

# Check login
ACCOUNT=$(az account show --query "user.name" -o tsv 2>/dev/null || true)
if [ -z "$ACCOUNT" ]; then
    echo -e "${YELLOW}Not logged in. Running 'az login'...${NC}"
    az login
fi

TENANT_ID=$(az account show --query "tenantId" -o tsv)
echo -e "Logged in as: ${GREEN}$(az account show --query 'user.name' -o tsv)${NC}"
echo -e "Tenant ID:    ${GREEN}${TENANT_ID}${NC}"
echo ""

# Prompt for redirect URIs
read -p "Production URL (e.g. https://sentinelwatcher.yourdomain.com): " PROD_URL
if [ -z "$PROD_URL" ]; then
    echo -e "${RED}Error: Production URL is required.${NC}"
    exit 1
fi

APP_NAME="SentinelWatcher"
DEV_URL="http://localhost:5173"

echo ""
echo -e "${YELLOW}Creating App Registration '${APP_NAME}'...${NC}"

# Create the app registration with SPA redirect URIs
APP_ID=$(az ad app create \
    --display-name "$APP_NAME" \
    --sign-in-audience "AzureADMyOrg" \
    --web-redirect-uris "" \
    --query "appId" -o tsv)

echo -e "Client ID: ${GREEN}${APP_ID}${NC}"

# Set SPA redirect URIs (az ad app create doesn't support --spa-redirect-uris directly)
az rest --method PATCH \
    --uri "https://graph.microsoft.com/v1.0/applications/$(az ad app show --id $APP_ID --query id -o tsv)" \
    --headers "Content-Type=application/json" \
    --body "{\"spa\":{\"redirectUris\":[\"${PROD_URL}\",\"${DEV_URL}\"]}}"

echo -e "${GREEN}SPA redirect URIs configured.${NC}"

# Expose an API - set Application ID URI
az ad app update --id "$APP_ID" --identifier-uris "api://${APP_ID}"

# Add the access_as_user scope
SCOPE_ID=$(cat /proc/sys/kernel/random/uuid 2>/dev/null || python3 -c "import uuid; print(uuid.uuid4())")
az rest --method PATCH \
    --uri "https://graph.microsoft.com/v1.0/applications/$(az ad app show --id $APP_ID --query id -o tsv)" \
    --headers "Content-Type=application/json" \
    --body "{
        \"api\": {
            \"oauth2PermissionScopes\": [{
                \"id\": \"${SCOPE_ID}\",
                \"adminConsentDescription\": \"Allows the app to access Azure Sentinel data on behalf of the signed-in user\",
                \"adminConsentDisplayName\": \"Access SentinelWatcher as user\",
                \"userConsentDescription\": \"Allow SentinelWatcher to access your Azure Sentinel workspaces\",
                \"userConsentDisplayName\": \"Access SentinelWatcher\",
                \"isEnabled\": true,
                \"type\": \"User\",
                \"value\": \"access_as_user\"
            }]
        }
    }"

echo -e "${GREEN}API scope 'access_as_user' created.${NC}"

# Add API permissions
# Microsoft Graph - User.Read (already added by default usually)
az ad app permission add --id "$APP_ID" \
    --api "00000003-0000-0000-c000-000000000000" \
    --api-permissions "e1fe6dd8-ba31-4d61-89e7-88639da4683d=Scope" 2>/dev/null || true

# Azure Service Management - user_impersonation
az ad app permission add --id "$APP_ID" \
    --api "797f4846-ba00-4fd7-ba43-dac1f8f63013" \
    --api-permissions "41094075-9dad-400e-a0bd-54e686782033=Scope"

echo -e "${GREEN}API permissions configured.${NC}"

# Grant admin consent
echo -e "${YELLOW}Granting admin consent...${NC}"
sleep 5  # Wait for propagation
az ad app permission admin-consent --id "$APP_ID" 2>/dev/null || {
    echo -e "${YELLOW}Warning: Could not grant admin consent automatically. Please grant consent manually in the Azure Portal.${NC}"
}

# Create client secret
echo -e "${YELLOW}Creating client secret...${NC}"
SECRET=$(az ad app credential reset \
    --id "$APP_ID" \
    --display-name "SentinelWatcher Backend" \
    --years 1 \
    --query "password" -o tsv)

# Create service principal
az ad sp create --id "$APP_ID" > /dev/null 2>&1 || true

echo ""
echo -e "${GREEN}============================================${NC}"
echo -e "${GREEN}  App Registration created successfully!    ${NC}"
echo -e "${GREEN}============================================${NC}"
echo ""
echo -e "  Tenant ID:     ${GREEN}${TENANT_ID}${NC}"
echo -e "  Client ID:     ${GREEN}${APP_ID}${NC}"
echo -e "  Client Secret: ${GREEN}${SECRET}${NC}"
echo -e "  API Scope:     ${GREEN}api://${APP_ID}/access_as_user${NC}"
echo ""
echo -e "${YELLOW}Save these values! The client secret will not be shown again.${NC}"
echo ""
echo -e "Backend environment variables:"
echo -e "  AzureAd__TenantId=${TENANT_ID}"
echo -e "  AzureAd__ClientId=${APP_ID}"
echo -e "  AzureAd__ClientSecret=${SECRET}"
echo ""
echo -e "Frontend msalConfig.ts:"
echo -e "  clientId: '${APP_ID}'"
echo -e "  authority: 'https://login.microsoftonline.com/${TENANT_ID}'"
echo -e "  scopes: ['api://${APP_ID}/access_as_user']"
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo -e "  1. Assign RBAC roles to users (see docs/app-registration-guide.md)"
echo -e "  2. Update backend appsettings.json or environment variables"
echo -e "  3. Update frontend src/auth/msalConfig.ts"
