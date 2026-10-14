import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { PublicClientApplication } from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import { msalConfig } from './auth/msalConfig';
import App from './App.tsx';
import './index.css';

export const msalInstance = new PublicClientApplication(msalConfig);

// Ensure MSAL is fully initialized (redirect handled, cache populated)
// before rendering the app — prevents 401 race on first API call.
msalInstance.initialize().then(() => {
  msalInstance.handleRedirectPromise().then(() => {
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <MsalProvider instance={msalInstance}>
          <App />
        </MsalProvider>
      </StrictMode>,
    );
  });
});
