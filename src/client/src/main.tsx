import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { MsalProvider } from '@azure/msal-react';
import { loadRuntimeConfig } from './config/runtimeConfig';
import { initMsal } from './auth/msal';
import StartupError from './components/StartupError';
import App from './App.tsx';
import './index.css';

const root = createRoot(document.getElementById('root')!);

loadRuntimeConfig()
  .then(initMsal)
  .then((msalInstance) => {
    root.render(
      <StrictMode>
        <MsalProvider instance={msalInstance}>
          <App />
        </MsalProvider>
      </StrictMode>,
    );
  })
  .catch((err: unknown) => {
    console.error(err);
    root.render(<StartupError message={err instanceof Error ? err.message : String(err)} />);
  });
