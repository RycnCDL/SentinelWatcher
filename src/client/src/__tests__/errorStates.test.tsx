// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { AxiosError, AxiosHeaders, type AxiosResponse } from 'axios';
import * as api from '../services/api';
import { LOAD_ERROR_TITLE } from '../services/loadError';
import Dashboard from '../pages/Dashboard';
import AnalyticsRules from '../pages/AnalyticsRules';
import Ingestion from '../pages/Ingestion';
import DataConnectors from '../pages/DataConnectors';
import DataCollectionRules from '../pages/DataCollectionRules';
import SentinelHealth from '../pages/SentinelHealth';
import type { DataConnectorsResponse, DcrResponse, IngestionResponse, AnalyticsRulesResponse } from '../types';

const { workspace } = vi.hoisted(() => ({
  workspace: {
    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.OperationalInsights/workspaces/law',
    workspaceName: 'law',
    subscriptionId: '00000000-0000-0000-0000-000000000000',
    resourceGroup: 'rg',
    sentinelEnabled: null,
  },
}));

vi.mock('../services/api');
vi.mock('../auth/useWorkspace', () => ({
  useWorkspace: () => ({
    workspaces: [workspace],
    selectedWorkspace: workspace,
    switchWorkspace: () => {},
    loading: false,
    error: null,
    accessDenied: false,
    refresh: () => {},
  }),
}));

// What the API answers when no Azure token can be acquired (the failed local run).
function azureFailure(): AxiosError {
  const response = {
    status: 502,
    statusText: 'Bad Gateway',
    headers: {},
    config: { headers: new AxiosHeaders() },
    data: {
      error: {
        code: 'AuthenticationFailed',
        message: "Could not get an Azure token for the app's identity. See the server log.",
      },
    },
  } as AxiosResponse;
  return new AxiosError('Request failed with status code 502', 'ERR_BAD_RESPONSE', undefined, undefined, response);
}

// Texts that claim everything is fine or show zeros. None may appear when loading failed.
const ALL_GOOD = [
  /All connectors receiving data/,
  /All receiving data/,
  /Health Monitoring Not Enabled/i,
  /\b0 rules\b/,
  /\b0(\.0+)? GB\b/,
  /\$0\.00/,
  /\b0 incidents\b/,
  /No incidents from existing analytics rules/,
  /No connectors match/,
];

function expectNoAllGoodTexts() {
  const text = document.body.textContent ?? '';
  for (const pattern of ALL_GOOD) expect(text).not.toMatch(pattern);
}

beforeAll(() => {
  // Recharts needs ResizeObserver, which jsdom does not provide.
  globalThis.ResizeObserver ??= class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('when every Azure call fails', () => {
  beforeEach(() => {
    vi.mocked(api.getAnalyticsRules).mockRejectedValue(azureFailure());
    vi.mocked(api.getIngestion).mockRejectedValue(azureFailure());
    vi.mocked(api.getDataConnectors).mockRejectedValue(azureFailure());
    vi.mocked(api.getDcr).mockRejectedValue(azureFailure());
    vi.mocked(api.getSentinelHealth).mockRejectedValue(azureFailure());
  });

  it('the Dashboard shows the error on every card and no "all good" text', async () => {
    render(<Dashboard />);

    expect(await screen.findByRole('alert')).toHaveProperty('textContent', expect.stringContaining(LOAD_ERROR_TITLE));
    expect(screen.getAllByText(LOAD_ERROR_TITLE)).toHaveLength(4); // one subtitle per KPI card
    expect(screen.getAllByText('Error')).toHaveLength(4);
    expect(document.body.textContent).toContain('AuthenticationFailed');
    expectNoAllGoodTexts();
  });

  it.each([
    ['Analytics Rules', AnalyticsRules],
    ['Log Ingestion', Ingestion],
    ['Data Connectors', DataConnectors],
    ['Collection Rules', DataCollectionRules],
    ['Sentinel Health', SentinelHealth],
  ])('the %s page shows the error state and no "all good" text', async (_name, Page) => {
    render(<Page />);

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain(LOAD_ERROR_TITLE);
    expect(alert.textContent).toContain('See the server log');
    expectNoAllGoodTexts();
  });
});

describe('positive control', () => {
  it('the Dashboard does show "All connectors receiving data" when the data really says so', async () => {
    const now = new Date().toISOString();
    vi.mocked(api.getAnalyticsRules).mockResolvedValue({
      top20MostIncidents: [], top20LeastIncidents: [], totalRules: 3, incidentsLoaded: true, totalIncidents: 2,
      incidentsFromExistingRules: 2, incidentsFromMissingRules: 0, incidentsWithoutRule: 0,
      missingRules: [], noRuleSources: [], lastRefreshed: now, incidentsError: null, refreshError: null,
    } satisfies AnalyticsRulesResponse);
    vi.mocked(api.getIngestion).mockResolvedValue({
      tables: [], totalVolumeMB: 2048, totalVolumeGB: 2, totalEstimatedCostUSD: 5.52, daysAnalyzed: 30,
      lastRefreshed: now, partialError: null, refreshError: null,
    } satisfies IngestionResponse);
    vi.mocked(api.getDataConnectors).mockResolvedValue({
      connectors: [{
        connectorId: 'syslog', connectorName: 'Syslog', connectorType: 'Syslog', status: 'Healthy',
        lastLogReceived: now, gapMinutes: 1, errorMessage: null, associatedTables: ['Syslog'], dataLakeTables: [],
      }],
      healthyCount: 1, warningCount: 0, errorCount: 0, quietCount: 0, lastChecked: now, partialError: null, refreshError: null,
    } satisfies DataConnectorsResponse);
    vi.mocked(api.getDcr).mockResolvedValue({
      dataCollectionRules: [], totalCount: 0, healthyCount: 0, warningCount: 0, errorCount: 0, lastChecked: now, refreshError: null,
    } satisfies DcrResponse);

    render(<Dashboard />);

    expect(await screen.findByText('All connectors receiving data')).toBeTruthy();
    expect(screen.queryByText(LOAD_ERROR_TITLE)).toBeNull();
  });
});
