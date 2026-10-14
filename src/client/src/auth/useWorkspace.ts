import { createContext, useContext } from 'react';

export interface WorkspaceInfo {
  subscriptionId: string;
  subscriptionName: string;
  resourceGroup: string;
  workspaceName: string;
  workspaceId: string;
  location: string;
  sentinelEnabled: boolean;
}

export interface WorkspaceContextType {
  workspaces: WorkspaceInfo[];
  selectedWorkspace: WorkspaceInfo | null;
  switchWorkspace: (workspace: WorkspaceInfo) => void;
  loading: boolean;
  error: string | null;
  refresh: () => void;
}

export const WorkspaceCtx = createContext<WorkspaceContextType>({
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
