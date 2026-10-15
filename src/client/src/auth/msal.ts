import { LogLevel, PublicClientApplication, type Configuration } from '@azure/msal-browser';
import type { RuntimeConfig } from '../config/runtimeConfig';

let msalInstance: PublicClientApplication | null = null;
let apiScopes: string[] = [];

// Creates and fully initializes MSAL (redirect handled, cache populated) before the
// app renders. This prevents a 401 race on the first API call.
export async function initMsal(config: RuntimeConfig): Promise<PublicClientApplication> {
  const configuration: Configuration = {
    auth: {
      clientId: config.clientId,
      authority: config.authority,
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

  const instance = new PublicClientApplication(configuration);
  await instance.initialize();
  await instance.handleRedirectPromise();

  msalInstance = instance;
  apiScopes = [config.apiScope];
  return instance;
}

export function getMsalInstance(): PublicClientApplication {
  if (!msalInstance) throw new Error('MSAL is not initialized.');
  return msalInstance;
}

// Scopes for the SentinelWatcher API (api://<client-id>/access_as_user).
export function getApiScopes(): string[] {
  return apiScopes;
}
