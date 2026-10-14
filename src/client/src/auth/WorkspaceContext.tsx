import { createContext, useContext, useState, useEffect, useCallback, type ReactNode } from 'react';
import { getWorkspaces } from '../services/api';

export interface WorkspaceInfo {
  subscriptionId: string;
  subscriptionName: string;
  resourceGroup: string;
  workspaceName: string;
  workspaceId: string;
  location: string;
  sentinelEnabled: boolean;
}

interface WorkspaceContextType {
  workspaces: WorkspaceInfo[];
  selectedWorkspace: WorkspaceInfo | null;
  switchWorkspace: (workspace: WorkspaceInfo) => void;
  loading: boolean;
  error: string | null;
  refresh: () => void;
}

const WorkspaceCtx = createContext<WorkspaceContextType>({
  workspaces: [],
  selectedWorkspace: null,
  switchWorkspace: () => {},
  loading: true,
  error: null,
  refresh: () => {},
});

export function useWorkspace() {
  return useContext(WorkspaceCtx);
}

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
    } catch (err: any) {
      // If 401, token may not be ready yet — retry once after a short delay
      if (err?.response?.status === 401 && !retried) {
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
