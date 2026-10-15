// @vitest-environment jsdom
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import * as api from '../services/api';
import Dashboard from '../pages/Dashboard';
import AnalyticsRules from '../pages/AnalyticsRules';
import type { AnalyticsRuleStats, AnalyticsRulesResponse } from '../types';

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

// The workspace from the local test: 35 enabled rules (34 Scheduled: High 4, Medium 19, Low 7,
// Informational 4, plus the Fusion rule with High). The API sends at most 20 rules per display list.
const SEVERITIES = [
  ...Array(5).fill('High'),
  ...Array(19).fill('Medium'),
  ...Array(7).fill('Low'),
  ...Array(4).fill('Informational'),
] as string[];

const rules: AnalyticsRuleStats[] = SEVERITIES.map((severity, i) => ({
  ruleId: `rule-${i + 1}`,
  ruleName: `Rule ${i + 1}`,
  severity,
  enabled: true,
  incidentCount: 0,
  lastIncident: null,
}));

// The breakdown shown when the counts came from the list: Medium 11, High 3, Low 3, Informational 3.
const pick = (severity: string, count: number) => rules.filter(r => r.severity === severity).slice(0, count);
const leastList = [...pick('Medium', 11), ...pick('High', 3), ...pick('Low', 3), ...pick('Informational', 3)];

const response: AnalyticsRulesResponse = {
  top20MostIncidents: [],
  top20LeastIncidents: leastList,
  totalRules: 35,
  enabledRules: 35,
  disabledRules: 0,
  rulesWithIncidents: 0,
  severityCounts: [
    { severity: 'High', total: 5, enabled: 5 },
    { severity: 'Medium', total: 19, enabled: 19 },
    { severity: 'Low', total: 7, enabled: 7 },
    { severity: 'Informational', total: 4, enabled: 4 },
  ],
  incidentsLoaded: true,
  totalIncidents: 104,
  incidentsFromExistingRules: 0,
  lastIncidentFromExistingRules: null,
  incidentsFromMissingRules: 0,
  incidentsWithoutRule: 104,
  missingRules: [],
  noRuleSources: [{ provider: 'Microsoft XDR', label: 'Defender XDR / no analytics rule', incidentCount: 104, lastIncident: null }],
  lastRefreshed: new Date().toISOString(),
  incidentsError: null,
  refreshError: null,
};

beforeAll(() => {
  // Recharts needs ResizeObserver, which jsdom does not provide.
  globalThis.ResizeObserver ??= class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
});

beforeEach(() => {
  vi.mocked(api.getAnalyticsRules).mockResolvedValue(response);
  // Only the rule counts matter here.
  vi.mocked(api.getIngestion).mockRejectedValue(new Error('not used'));
  vi.mocked(api.getDataConnectors).mockRejectedValue(new Error('not used'));
  vi.mocked(api.getDcr).mockRejectedValue(new Error('not used'));
});

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('rule counts with more than 20 rules', () => {
  it('the fixture lists only 20 rules, so counting the list would give 20', () => {
    expect(leastList).toHaveLength(20);
    expect(new Set(leastList.map(r => r.ruleId)).size).toBe(20);
  });

  it('the Dashboard counts all 35 rules, not the top 20 list', async () => {
    render(<Dashboard />);

    expect(await screen.findByText(/104 incidents · 35 enabled/)).toBeTruthy();
    expect(screen.getByText('● 35 enabled')).toBeTruthy();
    expect(document.body.textContent).not.toMatch(/\b20 enabled\b/);

    const breakdown = screen.getByText('Rule Severity Breakdown').closest('.section');
    expect(breakdown?.textContent).toContain('High5');
    expect(breakdown?.textContent).toContain('Medium19');
    expect(breakdown?.textContent).toContain('Low7');
    expect(breakdown?.textContent).toContain('Informational4');
    expect(breakdown?.textContent).toContain('Enabled100%');
  });

  it('the Analytics Rules page says the least incidents list is cut at 20', async () => {
    render(<AnalyticsRules />);

    expect(await screen.findByText(/35 rules \(35 enabled\)/)).toBeTruthy();
    expect(screen.getByText('Showing 20 of 35 enabled rules.')).toBeTruthy();
  });
});

describe('Where incidents come from', () => {
  it('shows the newest incident of existing rules in the "Existing analytics rules" row', async () => {
    const newest = '2026-10-15T09:08:12Z';
    vi.mocked(api.getAnalyticsRules).mockResolvedValue({
      ...response,
      incidentsFromExistingRules: 48,
      lastIncidentFromExistingRules: newest,
      totalIncidents: 152,
    });

    render(<AnalyticsRules />);

    const row = (await screen.findByText(/^Existing analytics rules/)).closest('tr');
    expect(row?.textContent).toContain('48');
    expect(row?.textContent).toContain(new Date(newest).toLocaleString());
  });

  it('shows "-" when existing rules have no incidents', async () => {
    render(<AnalyticsRules />);

    const row = (await screen.findByText(/^Existing analytics rules/)).closest('tr');
    expect(row?.lastElementChild?.textContent).toBe('-');
  });
});
