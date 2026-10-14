import { useIsAuthenticated, useMsal } from '@azure/msal-react';
import { InteractionStatus } from '@azure/msal-browser';
import { loginRequest } from './msalConfig';
import { ShieldAlert } from 'lucide-react';
import type { ReactNode } from 'react';

interface AuthGuardProps {
  children: ReactNode;
}

export default function AuthGuard({ children }: AuthGuardProps) {
  const isAuthenticated = useIsAuthenticated();
  const { instance, inProgress } = useMsal();

  if (inProgress !== InteractionStatus.None) {
    return (
      <div className="auth-loading">
        <ShieldAlert size={48} />
        <p>Authenticating...</p>
      </div>
    );
  }

  if (!isAuthenticated) {
    return (
      <div className="auth-login">
        <div className="auth-login__card">
          <ShieldAlert size={48} className="auth-login__icon" />
          <h1>Sentinel Watcher</h1>
          <p>Sign in with your Azure AD account to monitor your Microsoft Sentinel workspaces.</p>
          <button
            className="auth-login__button"
            onClick={() => instance.loginRedirect(loginRequest)}
          >
            Sign in with Microsoft
          </button>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
