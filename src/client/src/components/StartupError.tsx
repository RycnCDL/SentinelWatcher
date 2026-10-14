import { ShieldAlert } from 'lucide-react';

// Shown when the app cannot start, for example because /config.json is unavailable.
export default function StartupError({ message }: { message: string }) {
  return (
    <div className="auth-login">
      <div className="auth-login__card">
        <ShieldAlert size={48} className="auth-login__icon" />
        <h1>SentinelWatcher could not start</h1>
        <p>{message}</p>
      </div>
    </div>
  );
}
