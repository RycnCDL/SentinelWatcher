import { createContext, useContext } from 'react';

// A workspace from the server-side allow-list. resourceId identifies it in API calls.
export interface WorkspaceInfo {
  resourceId: string;
  workspaceName: string;
  subscriptionId: string;
  resourceGroup: string;
  // null when the status could not be read.
  sentinelEnabled: boolean | null;
}

export interface WorkspaceContextType {
  workspaces: WorkspaceInfo[];
  selectedWorkspace: WorkspaceInfo | null;
  switchWorkspace: (workspace: WorkspaceInfo) => void;
  loading: boolean;
  error: string | null;
  // True when the API returns 403: the user is signed in but lacks the Viewer app role.
  accessDenied: boolean;
  refresh: () => void;
}

export const WorkspaceCtx = createContext<WorkspaceContextType>({
  workspaces: [],
  selectedWorkspace: null,
  switchWorkspace: () => {},
  loading: true,
  error: null,
  accessDenied: false,
  refresh: () => {},
});

export function useWorkspace() {
  return useContext(WorkspaceCtx);
}
