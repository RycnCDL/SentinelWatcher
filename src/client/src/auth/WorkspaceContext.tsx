import { useState, useEffect, useCallback, type ReactNode } from 'react';
import { isAxiosError } from 'axios';
import { getWorkspaces } from '../services/api';
import { WorkspaceCtx, type WorkspaceInfo } from './useWorkspace';

const STORAGE_KEY = 'sw-selected-workspace';

export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const [workspaces, setWorkspaces] = useState<WorkspaceInfo[]>([]);
  const [selectedWorkspace, setSelectedWorkspace] = useState<WorkspaceInfo | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const loadWorkspaces = useCallback(async (retried = false) => {
    setLoading(true);
    setError(null);
    try {
      const data = await getWorkspaces();
      setWorkspaces(data);

      // Restore last selected workspace from localStorage
      const savedId = localStorage.getItem(STORAGE_KEY);
      const saved = data.find((ws: WorkspaceInfo) => ws.workspaceId === savedId);

      if (saved) {
        setSelectedWorkspace(saved);
      } else if (data.length > 0) {
        setSelectedWorkspace(data[0]);
        localStorage.setItem(STORAGE_KEY, data[0].workspaceId);
      }
    } catch (err: unknown) {
      // If 401, token may not be ready yet — retry once after a short delay
      if (isAxiosError(err) && err.response?.status === 401 && !retried) {
        setTimeout(() => loadWorkspaces(true), 1000);
        return;
      }
      setError('Failed to load workspaces');
      console.error(err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadWorkspaces();
  }, [loadWorkspaces]);

  const switchWorkspace = useCallback((workspace: WorkspaceInfo) => {
    setSelectedWorkspace(workspace);
    localStorage.setItem(STORAGE_KEY, workspace.workspaceId);
  }, []);

  return (
    <WorkspaceCtx.Provider
      value={{ workspaces, selectedWorkspace, switchWorkspace, loading, error, refresh: loadWorkspaces }}
    >
      {children}
    </WorkspaceCtx.Provider>
  );
}
