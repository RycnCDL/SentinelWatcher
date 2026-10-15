import { AlertTriangle, Loader } from 'lucide-react';
import type { LoadError } from '../types';
import { LOAD_ERROR_TITLE } from '../services/loadError';

// Shown instead of a section's data when it could not be loaded. A failure must never look
// like zeros or like "all good".
export function LoadErrorState({ error, what }: { error: LoadError; what?: string }) {
  if (error.code === 'Loading') {
    return (
      <div className="section load-state" role="status">
        <Loader size={20} />
        <span>{error.message}</span>
      </div>
    );
  }

  return (
    <div className="section load-error" role="alert">
      <div className="load-error__title">
        <AlertTriangle size={20} />
        <strong>{LOAD_ERROR_TITLE}</strong>
      </div>
      <p className="load-error__message">
        {what && <>{what}: </>}
        {error.message}
      </p>
      <span className="load-error__code">{error.code}</span>
    </div>
  );
}

// Older data is shown, but the latest refresh failed.
export function StaleDataBanner({ error, since }: { error: LoadError | null | undefined; since?: string }) {
  if (!error) return null;
  return (
    <div className="alert alert--warning" role="status">
      <AlertTriangle size={14} />
      <span>
        The latest refresh failed, showing older data
        {since && <> from {new Date(since).toLocaleTimeString()}</>}. {error.message}
      </span>
    </div>
  );
}

// The data is valid but incomplete.
export function PartialDataBanner({ error }: { error: LoadError | null | undefined }) {
  if (!error) return null;
  return (
    <div className="alert alert--warning" role="status">
      <AlertTriangle size={14} />
      <span>{error.message}</span>
    </div>
  );
}
