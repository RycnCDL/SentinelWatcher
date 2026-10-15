// @vitest-environment jsdom
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import * as api from '../services/api';
import SentinelHealth from '../pages/SentinelHealth';
import type { SentinelHealthResponse } from '../types';

const { workspace } = vi.hoisted(() => ({
  workspace: {
    resourceId: '/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.OperationalInsights/workspaces/law',
    workspaceName: 'law',
    subscriptionId: '00000000-0000-0000-0000-000000000000',
    resourceGroup: 'rg',
    sentinelEnabled: true,
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

describe('Sentinel Health with more than 50 failures', () => {
  it('says that the failure list shows only the newest 50', async () => {
    const now = new Date().toISOString();
    vi.mocked(api.getSentinelHealth).mockResolvedValue({
      queryTime: now,
      healthMonitoringEnabled: true,
      healthScore: 86,
      totalConnectors: 0, healthyConnectors: 0, staleConnectors: 0,
      totalRuleRuns: 500, failedRuleRuns: 70, autoDisabledRulesCount: 0,
      totalAutomationRuns: 10, failedAutomationRuns: 3,
      autoDisabledRules: [],
      trend: [],
      connectorStatuses: [],
      failuresByType: [
        { resourceType: 'Analytics Rule', count: 70 },
        { resourceType: 'Playbook', count: 3 },
      ],
      totalFailures: 73,
      recentFailures: Array.from({ length: 50 }, (_, i) => ({
        timeGenerated: now, resourceType: 'Analytics Rule', resourceName: `Rule ${i}`,
        kind: 'Scheduled', status: 'Failure', category: 'Other',
      })),
    } satisfies SentinelHealthResponse);

    render(<SentinelHealth />);

    expect(await screen.findByText('Showing the 50 newest of 73 failures.')).toBeTruthy();
    expect(screen.queryByText(/No failures in the last 24 hours/)).toBeNull();
  });
});
