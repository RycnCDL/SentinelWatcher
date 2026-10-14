#!/usr/bin/env bash
# ==============================================================================
# SentinelWatcher: Microsoft Entra app registration setup
# ==============================================================================
# Creates the app registration that users sign in with. It:
#   - registers a single-page application (SPA) with the given redirect URIs
#   - exposes the API scope api://<client-id>/access_as_user and pre-authorizes
#     the app itself for it, so users see no consent prompt for SentinelWatcher
#   - defines the app role "SentinelWatcher.Viewer"
#   - creates the enterprise application (service principal) and turns on
#     "Assignment required", so only users and groups that are assigned the
#     Viewer role can sign in at all
#   - optionally assigns the Viewer role to you (--assign-me)
#
# No client secret is created: the backend calls Azure with a managed identity
# (or with your az login during local development), not with this app.
#
# Usage:
#   bash scripts/setup-app-registration.sh [--name NAME] [--redirect-uri URL]... [--assign-me]
#
# Examples:
#   bash scripts/setup-app-registration.sh --assign-me
#   bash scripts/setup-app-registration.sh --redirect-uri https://sentinelwatcher.example.com --assign-me
#
# The default redirect URI http://localhost:5173 (local development) is always added.
#
# Requirements: Azure CLI, signed in (az login) to the target tenant with a role that
# can create app registrations and assign app roles, for example Application
# Administrator or Cloud Application Administrator. Runs in Azure Cloud Shell,
# Linux, macOS and Git Bash on Windows.
# ==============================================================================

set -euo pipefail

APP_NAME="SentinelWatcher"
REDIRECT_URIS=("http://localhost:5173")
ASSIGN_ME=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --name) APP_NAME="$2"; shift 2 ;;
    --redirect-uri) REDIRECT_URIS+=("${2%/}"); shift 2 ;;
    --assign-me) ASSIGN_ME=true; shift ;;
    -h|--help) sed -n '2,32p' "$0"; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; exit 1 ;;
  esac
done

VIEWER_ROLE="SentinelWatcher.Viewer"
GRAPH="https://graph.microsoft.com/v1.0"

# Azure CLI on Windows prints CRLF line endings; strip carriage returns from values.
azv() { az "$@" | tr -d '\r'; }

# Tries several generators and accepts only a well-formed UUID (on Windows, "python3"
# can be a non-working Microsoft Store alias, so a generator existing is not enough).
new_uuid() {
  local id="" pattern='^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
  if command -v uuidgen >/dev/null 2>&1; then
    id="$(uuidgen 2>/dev/null | tr 'A-Z' 'a-z' | tr -d '\r')" || true
  fi
  if [[ ! $id =~ $pattern && -r /proc/sys/kernel/random/uuid ]]; then
    id="$(cat /proc/sys/kernel/random/uuid)" || true
  fi
  if [[ ! $id =~ $pattern ]] && command -v python3 >/dev/null 2>&1; then
    id="$(python3 -c 'import uuid; print(uuid.uuid4())' 2>/dev/null | tr -d '\r')" || true
  fi
  if [[ ! $id =~ $pattern ]] && command -v powershell.exe >/dev/null 2>&1; then
    id="$(powershell.exe -NoProfile -Command '[guid]::NewGuid().ToString()' 2>/dev/null | tr -d '\r')" || true
  fi
  if [[ ! $id =~ $pattern ]]; then
    echo "Could not generate a UUID. Install uuidgen or Python 3, or run the script in Azure Cloud Shell." >&2
    return 1
  fi
  echo "$id"
}

# Graph request bodies are written to a file in the current directory, which works
# for every Azure CLI build (including the Windows one under Git Bash).
BODY_FILE="$(mktemp ./sw-graph-XXXXXX)"
trap 'rm -f "$BODY_FILE"' EXIT
graph_patch() { az rest --method PATCH --uri "$1" --headers "Content-Type=application/json" --body "@$BODY_FILE" --output none; }

if ! command -v az >/dev/null 2>&1; then
  echo "Azure CLI (az) is not installed: https://learn.microsoft.com/cli/azure/install-azure-cli" >&2
  exit 1
fi
if ! az account show --output none 2>/dev/null; then
  echo "Not signed in. Run 'az login' first." >&2
  exit 1
fi

TENANT_ID="$(azv account show --query tenantId -o tsv)"
echo "Tenant:        $TENANT_ID"
echo "Signed in as:  $(azv account show --query user.name -o tsv)"
echo

# 1. App registration (single tenant).
echo "Creating app registration '$APP_NAME'..."
APP_ID="$(azv ad app create --display-name "$APP_NAME" --sign-in-audience AzureADMyOrg --query appId -o tsv)"
APP_OBJECT_ID="$(azv ad app show --id "$APP_ID" --query id -o tsv)"
SCOPE_ID="$(new_uuid)"
ROLE_ID="$(new_uuid)"

REDIRECTS_JSON="$(printf '"%s",' "${REDIRECT_URIS[@]}")"
REDIRECTS_JSON="[${REDIRECTS_JSON%,}]"

# 2. SPA redirect URIs, API scope (v2 access tokens) and the Viewer app role.
cat > "$BODY_FILE" <<EOF
{
  "identifierUris": ["api://$APP_ID"],
  "spa": { "redirectUris": $REDIRECTS_JSON },
  "api": {
    "requestedAccessTokenVersion": 2,
    "oauth2PermissionScopes": [{
      "id": "$SCOPE_ID",
      "value": "access_as_user",
      "type": "User",
      "isEnabled": true,
      "adminConsentDisplayName": "Access SentinelWatcher",
      "adminConsentDescription": "Allows the SentinelWatcher web app to call the SentinelWatcher API as the signed-in user.",
      "userConsentDisplayName": "Access SentinelWatcher",
      "userConsentDescription": "Allows SentinelWatcher to show you Microsoft Sentinel insights."
    }]
  },
  "appRoles": [{
    "id": "$ROLE_ID",
    "value": "$VIEWER_ROLE",
    "displayName": "SentinelWatcher Viewer",
    "description": "Can view the SentinelWatcher dashboards.",
    "allowedMemberTypes": ["User"],
    "isEnabled": true
  }]
}
EOF
graph_patch "$GRAPH/applications/$APP_OBJECT_ID"
echo "Configured redirect URIs, API scope and app role."

# 3. Pre-authorize the app for its own scope, so users get no consent prompt.
#    The whole "api" object is sent again because PATCH replaces it.
cat > "$BODY_FILE" <<EOF
{
  "api": {
    "requestedAccessTokenVersion": 2,
    "oauth2PermissionScopes": [{
      "id": "$SCOPE_ID",
      "value": "access_as_user",
      "type": "User",
      "isEnabled": true,
      "adminConsentDisplayName": "Access SentinelWatcher",
      "adminConsentDescription": "Allows the SentinelWatcher web app to call the SentinelWatcher API as the signed-in user.",
      "userConsentDisplayName": "Access SentinelWatcher",
      "userConsentDescription": "Allows SentinelWatcher to show you Microsoft Sentinel insights."
    }],
    "preAuthorizedApplications": [{ "appId": "$APP_ID", "delegatedPermissionIds": ["$SCOPE_ID"] }]
  }
}
EOF
graph_patch "$GRAPH/applications/$APP_OBJECT_ID"
echo "Pre-authorized the app for its access_as_user scope."

# 4. Enterprise application with "Assignment required".
SP_ID="$(azv ad sp create --id "$APP_ID" --query id -o tsv)"
echo '{ "appRoleAssignmentRequired": true }' > "$BODY_FILE"
graph_patch "$GRAPH/servicePrincipals/$SP_ID"
echo "Created the enterprise application and set 'Assignment required'."

# 5. Optionally assign the Viewer role to the signed-in user (retries while the new
#    service principal propagates).
if [[ "$ASSIGN_ME" == true ]]; then
  USER_ID="$(azv ad signed-in-user show --query id -o tsv)"
  cat > "$BODY_FILE" <<EOF
{ "principalId": "$USER_ID", "resourceId": "$SP_ID", "appRoleId": "$ROLE_ID" }
EOF
  for attempt in 1 2 3 4 5 6; do
    if az rest --method POST --uri "$GRAPH/servicePrincipals/$SP_ID/appRoleAssignedTo" \
         --headers "Content-Type=application/json" --body "@$BODY_FILE" --output none 2>/dev/null; then
      echo "Assigned $VIEWER_ROLE to you."
      break
    fi
    if [[ $attempt -eq 6 ]]; then
      echo "Could not assign the role yet. Assign it in the Entra admin center: Enterprise applications, $APP_NAME, Users and groups." >&2
    else
      sleep 10
    fi
  done
fi

cat <<EOF

============================================================
App registration created
============================================================
Tenant ID:   $TENANT_ID
Client ID:   $APP_ID
API scope:   api://$APP_ID/access_as_user
App role:    $VIEWER_ROLE (Assignment required: yes)
Redirects:   ${REDIRECT_URIS[*]}

Backend settings (appsettings.Development.json or App Service app settings):
  AzureAd__TenantId=$TENANT_ID
  AzureAd__ClientId=$APP_ID

No client secret is needed. Assign the $VIEWER_ROLE role to the users or
groups who should see SentinelWatcher: Entra admin center, Enterprise
applications, $APP_NAME, Users and groups, Add user/group.
EOF
