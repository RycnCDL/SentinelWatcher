import { type Configuration, LogLevel } from '@azure/msal-browser';

// These values must be configured per deployment.
// Update them with your Azure AD App Registration details.
const clientId = import.meta.env.VITE_AZURE_CLIENT_ID || '';
const tenantId = import.meta.env.VITE_AZURE_TENANT_ID || '';

export const msalConfig: Configuration = {
  auth: {
    clientId,
    authority: `https://login.microsoftonline.com/${tenantId}`,
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
  },
  cache: {
    cacheLocation: 'localStorage',
  },
  system: {
    loggerOptions: {
      loggerCallback: (level, message) => {
        if (level === LogLevel.Error) {
          console.error(message);
        }
      },
      logLevel: LogLevel.Error,
    },
  },
};

export const loginRequest = {
  scopes: [`api://${clientId}/access_as_user`],
};

export const apiScopes = {
  accessAsUser: [`api://${clientId}/access_as_user`],
};
