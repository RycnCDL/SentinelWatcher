import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { isAxiosError } from 'axios';
import { getWorkspaces } from '../services/api';
import { WorkspaceCtx, type WorkspaceInfo } from './useWorkspace';

const STORAGE_KEY = 'sw-selected-workspace';

// If the first call returns 401, the token may not be ready yet: retry once after a short delay.
async function getWorkspacesWithRetry(): Promise<WorkspaceInfo[]> {
  try {
    return await getWorkspaces();
  } catch (err: unknown) {
    if (!isAxiosError(err) || err.response?.status !== 401) throw err;
    await new Promise((resolve) => setTimeout(resolve, 1000));
    return await getWorkspaces();
  }
}

export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [workspaces, setWorkspaces] = useState<WorkspaceInfo[]>([]);
  const [selectedWorkspace, setSelectedWorkspace] = useState<WorkspaceInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [accessDenied, setAccessDenied] = useState(false);

  // Sets state only in promise callbacks, so it can run directly from the mount effect.
  const fetchWorkspaces = useCallback(() =>
    getWorkspacesWithRetry()
      .then((data) => {
        setWorkspaces(data);

        // Restore last selected workspace from localStorage
        const savedId = localStorage.getItem(STORAGE_KEY);
        const saved = data.find((ws: WorkspaceInfo) => ws.resourceId === savedId);

        if (saved) {
          setSelectedWorkspace(saved);
        } else if (data.length > 0) {
          setSelectedWorkspace(data[0]);
          localStorage.setItem(STORAGE_KEY, data[0].resourceId);
        }
      })
      .catch((err: unknown) => {
        if (isAxiosError(err) && err.response?.status === 403) {
          setAccessDenied(true);
        }
        setError('Failed to load workspaces');
        console.error(err);
      })
      .finally(() => setLoading(false)), []);

  useEffect(() => {
    fetchWorkspaces();
  }, [fetchWorkspaces]);

  const refresh = useCallback(() => {
    setLoading(true);
    setError(null);
    setAccessDenied(false);
    fetchWorkspaces();
  }, [fetchWorkspaces]);

  const switchWorkspace = useCallback((workspace: WorkspaceInfo) => {
    setSelectedWorkspace(workspace);
    localStorage.setItem(STORAGE_KEY, workspace.resourceId);
  }, []);

  return (
    <WorkspaceCtx.Provider
      value={{ workspaces, selectedWorkspace, switchWorkspace, loading, error, accessDenied, refresh }}
    >
      {children}
    </WorkspaceCtx.Provider>
  );
}
