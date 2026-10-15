import { useMsal } from '@azure/msal-react';
import { ShieldAlert } from 'lucide-react';
import type { ReactNode } from 'react';
import { useWorkspace } from './useWorkspace';

// Replaces the app with an explanation when the signed-in user lacks the Viewer app role.
export default function AccessGate({ children }: { children: ReactNode }) {
  const { accessDenied } = useWorkspace();
  const { instance, accounts } = useMsal();

  if (!accessDenied) return <>{children}</>;

  return (
    <div className="auth-login">
      <div className="auth-login__card">
        <ShieldAlert size={48} className="auth-login__icon" />
        <h1>Access not assigned</h1>
        <p>
          You are signed in as <strong>{accounts[0]?.username ?? 'an unknown account'}</strong>, but this
          account does not have the <code>SentinelWatcher.Viewer</code> role. Ask an administrator to assign
          it to you in Microsoft Entra ID (Enterprise applications, SentinelWatcher, Users and groups), then
          sign in again.
        </p>
        <button className="auth-login__button" onClick={() => instance.logoutRedirect()}>
          Sign out
        </button>
      </div>
    </div>
  );
}
