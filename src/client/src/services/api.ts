import axios from 'axios';
import { apiScopes } from '../auth/msalConfig';
import { msalInstance } from '../main';
import type { WorkspaceInfo } from '../auth/WorkspaceContext';
import type {
  AnalyticsRulesResponse,
  IngestionResponse,
  DataConnectorsResponse,
  DcrResponse,
  SentinelHealthResponse,
} from '../types';

const api = axios.create({
  baseURL: import.meta.env.DEV ? 'http://localhost:5000/api' : '/api',
  timeout: 60000,
});

// Auth interceptor — acquire token silently and attach to every request
api.interceptors.request.use(async (config) => {
  try {
    const accounts = msalInstance.getAllAccounts();
    if (accounts.length > 0) {
      const response = await msalInstance.acquireTokenSilent({
        scopes: apiScopes.accessAsUser,
        account: accounts[0],
      });
      config.headers.Authorization = `Bearer ${response.accessToken}`;
    }
  } catch (err) {
    console.error('Token acquisition failed', err);
  }
  return config;
});

function wsParams(ws: WorkspaceInfo): string {
  return `subscriptionId=${encodeURIComponent(ws.subscriptionId)}&resourceGroup=${encodeURIComponent(ws.resourceGroup)}&workspaceName=${encodeURIComponent(ws.workspaceName)}&workspaceId=${encodeURIComponent(ws.workspaceId)}`;
}

// ---- Workspace-independent endpoints ----

export async function getWorkspaces(): Promise<WorkspaceInfo[]> {
  const { data } = await api.get<WorkspaceInfo[]>('/workspaces');
  return data;
}

export async function getCurrentUser(): Promise<{ name: string; email: string; objectId: string; tenantId: string }> {
  const { data } = await api.get('/auth/me');
  return data;
}

// ---- Workspace-specific endpoints ----

export async function getAnalyticsRules(ws: WorkspaceInfo): Promise<AnalyticsRulesResponse> {
  const { data } = await api.get<AnalyticsRulesResponse>(`/analyticsrules?${wsParams(ws)}`);
  return data;
}

export async function refreshAnalyticsRules(ws: WorkspaceInfo): Promise<void> {
  await api.post(`/analyticsrules/refresh?${wsParams(ws)}`);
}

export async function getIngestion(ws: WorkspaceInfo): Promise<IngestionResponse> {
  const { data } = await api.get<IngestionResponse>(`/ingestion?${wsParams(ws)}`);
  return data;
}

export async function getDataConnectors(ws: WorkspaceInfo): Promise<DataConnectorsResponse> {
  const { data } = await api.get<DataConnectorsResponse>(`/dataconnectors?${wsParams(ws)}`);
  return data;
}

export async function getDcr(ws: WorkspaceInfo): Promise<DcrResponse> {
  const { data } = await api.get<DcrResponse>(`/dcr?${wsParams(ws)}`);
  return data;
}

export async function getSentinelHealth(ws: WorkspaceInfo, days: number = 7): Promise<SentinelHealthResponse> {
  const { data } = await api.get<SentinelHealthResponse>(`/sentinelhealth?${wsParams(ws)}&days=${days}`);
  return data;
}
