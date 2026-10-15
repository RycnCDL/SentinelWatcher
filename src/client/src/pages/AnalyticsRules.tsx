import { useEffect, useState, useCallback } from 'react';
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Cell,
} from 'recharts';
import { RefreshCw, ShieldCheck } from 'lucide-react';
import { useWorkspace } from '../auth/useWorkspace';
import { getAnalyticsRules, refreshAnalyticsRules } from '../services/api';
import { useChartTheme } from '../hooks/useTheme';
import type { AnalyticsRulesResponse, LoadError } from '../types';
import { LoadErrorState, StaleDataBanner } from '../components/LoadErrorState';
import { loadErrorOf } from '../services/loadError';


const SEVERITY_COLORS: Record<string, string> = {
  High: '#FF4D4D',
  Medium: '#F59E0B',
  Low: '#3B82F6',
  Informational: '#6B7280',
  Unknown: '#6B7280',
};

function formatDate(iso: string | null, fallback = '-') {
  return iso ? new Date(iso).toLocaleString() : fallback;
}

// Where the incidents of the last 30 days come from: existing rules, rules that no longer
// exist, and incidents without an analytics rule. The rows add up to the total, except when
// an incident references several rules (it then counts once per rule).
function IncidentSources({ data }: { data: AnalyticsRulesResponse }) {
  const sum = data.incidentsFromExistingRules + data.incidentsFromMissingRules + data.incidentsWithoutRule;

  return (
    <div className="section">
      <h3>Where incidents come from (30 days)</h3>
      <table className="data-table">
        <thead>
          <tr>
            <th>Source</th>
            <th className="text-right">Incidents</th>
            <th>Last Incident</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Existing analytics rules ({data.totalRules})</td>
            <td className="text-right">{data.incidentsFromExistingRules}</td>
            <td>-</td>
          </tr>
          {data.missingRules.map((m) => (
            <tr key={m.ruleId}>
              <td>
                Rule deleted or not found
                <div className="meta-text" style={{ marginTop: '0.2rem' }}>
                  ID {m.ruleId}
                  {m.sampleIncidentTitle && <> | e.g. incident "{m.sampleIncidentTitle}"</>}
                </div>
              </td>
              <td className="text-right">{m.incidentCount}</td>
              <td>{formatDate(m.lastIncident)}</td>
            </tr>
          ))}
          {data.noRuleSources.map((s) => (
            <tr key={s.provider}>
              <td>{s.label}</td>
              <td className="text-right">{s.incidentCount}</td>
              <td>{formatDate(s.lastIncident)}</td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td><strong>Total incidents</strong></td>
            <td className="text-right"><strong>{data.totalIncidents}</strong></td>
            <td />
          </tr>
        </tfoot>
      </table>
      {sum > data.totalIncidents && (
        <p className="meta-text" style={{ marginTop: '0.75rem' }}>
          Some incidents reference several analytics rules and are counted once per rule above, so the
          rows add up to {sum}.
        </p>
      )}
    </div>
  );
}

export default function AnalyticsRules() {
  const { selectedWorkspace } = useWorkspace();
  const { tooltipStyle, gridStroke, axisStroke, axisTickFill } = useChartTheme();
  const [data, setData] = useState<AnalyticsRulesResponse | null>(null);
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<LoadError | null>(null);
  const loading = !selectedWorkspace || settledKey !== selectedWorkspace.resourceId;

  // State is only set in promise callbacks, so this can run directly from the effect.
  const fetchData = useCallback((): Promise<void> => {
    if (!selectedWorkspace) return Promise.resolve();
    const ws = selectedWorkspace;
    return getAnalyticsRules(ws)
      .then(
        (result) => {
          setData(result);
          setError(null);
        },
        (err: unknown) => {
          setData(null);
          setError(loadErrorOf(err));
        },
      )
      .finally(() => setSettledKey(ws.resourceId));
  }, [selectedWorkspace]);

  async function handleRefresh() {
    if (!selectedWorkspace) return;
    setRefreshing(true);
    try {
      await refreshAnalyticsRules(selectedWorkspace);
      await fetchData();
    } finally {
      setRefreshing(false);
    }
  }

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 60000);
    return () => clearInterval(interval);
  }, [fetchData]);

  if (loading) return <div className="loading">Loading analytics rules...</div>;
  if (!data) {
    return (
      <div className="page">
        <div className="page-header">
          <h2 className="page-title">Analytics Rules</h2>
        </div>
        {error && <LoadErrorState error={error} what="Analytics rules" />}
      </div>
    );
  }

  const topChartData = data.top20MostIncidents.map((r) => ({
    name: r.ruleName.length > 40 ? r.ruleName.slice(0, 40) + '...' : r.ruleName,
    incidents: r.incidentCount,
    severity: r.severity,
  }));

  return (
    <div className="page">
      <div className="page-header">
        <h2 className="page-title">Analytics Rules</h2>
        <div className="page-meta">
          <span className="meta-text">
            {data.totalRules} rules ({data.enabledRules} enabled) |{' '}
            {data.incidentsLoaded
              ? `${data.totalIncidents} incidents (30d)`
              : data.incidentsError ? 'incident counts unavailable' : 'loading incidents...'} |
            Last refresh: {new Date(data.lastRefreshed).toLocaleTimeString()}
          </span>
          <button
            className="btn btn--icon"
            onClick={handleRefresh}
            disabled={refreshing}
            title="Refresh"
          >
            <RefreshCw size={16} className={refreshing ? 'spinning' : ''} />
          </button>
        </div>
      </div>

      <StaleDataBanner error={data.refreshError} since={data.lastRefreshed} />

      {data.incidentsLoaded && <IncidentSources data={data} />}

      <div className="section">
        <h3>Top 20 Rules: Most Incidents</h3>
        {data.incidentsLoaded && data.rulesWithIncidents > data.top20MostIncidents.length && (
          <p className="meta-text">
            Showing {data.top20MostIncidents.length} of {data.rulesWithIncidents} rules with incidents.
          </p>
        )}
        {data.incidentsError ? (
          <LoadErrorState error={data.incidentsError} what="Incident counts" />
        ) : !data.incidentsLoaded ? (
          <p className="meta-text" style={{ textAlign: 'center', padding: '2rem 0' }}>Loading incident counts...</p>
        ) : topChartData.length === 0 ? (
          <div style={{ textAlign: 'center', padding: '2.5rem 1rem' }}>
            <ShieldCheck size={36} style={{ color: 'var(--text-muted)', marginBottom: '0.75rem' }} />
            <p style={{ color: 'var(--text-primary)', fontWeight: 600, margin: 0 }}>
              No incidents from existing analytics rules in the last 30 days
            </p>
            {data.totalIncidents > 0 && (
              <p className="meta-text" style={{ marginTop: '0.5rem' }}>
                All {data.totalIncidents} incidents come from deleted rules or have no analytics rule. See the
                table above.
              </p>
            )}
          </div>
        ) : (
          <>
            <div className="chart-container">
              <ResponsiveContainer width="100%" height={Math.max(200, topChartData.length * 26)}>
                <BarChart data={topChartData} layout="vertical" margin={{ left: 200 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke={gridStroke} />
                  <XAxis type="number" allowDecimals={false} stroke={axisStroke} tick={{ fill: axisStroke, fontSize: 12 }} />
                  <YAxis
                    type="category"
                    dataKey="name"
                    stroke={axisStroke}
                    width={190}
                    tick={{ fill: axisTickFill, fontSize: 12 }}
                  />
                  <Tooltip contentStyle={tooltipStyle} />
                  <Bar dataKey="incidents" radius={[0, 4, 4, 0]}>
                    {topChartData.map((entry, index) => (
                      <Cell
                        key={`cell-${index}`}
                        fill={SEVERITY_COLORS[entry.severity] || '#3B82F6'}
                      />
                    ))}
                  </Bar>
                </BarChart>
              </ResponsiveContainer>
            </div>

            <table className="data-table">
              <thead>
                <tr>
                  <th>Rule Name</th>
                  <th>Severity</th>
                  <th>Enabled</th>
                  <th className="text-right">Incidents</th>
                  <th>Last Incident</th>
                </tr>
              </thead>
              <tbody>
                {data.top20MostIncidents.map((r) => (
                  <tr key={r.ruleId}>
                    <td>{r.ruleName}</td>
                    <td>
                      <span className={`badge badge--${r.severity.toLowerCase()}`}>
                        {r.severity}
                      </span>
                    </td>
                    <td>{r.enabled ? 'Yes' : 'No'}</td>
                    <td className="text-right">{r.incidentCount}</td>
                    <td>{formatDate(r.lastIncident)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </>
        )}
      </div>

      <div className="section">
        <h3>Top 20 Rules: Least / No Incidents (Enabled)</h3>
        {data.enabledRules > data.top20LeastIncidents.length && (
          <p className="meta-text">
            Showing {data.top20LeastIncidents.length} of {data.enabledRules} enabled rules.
          </p>
        )}
        <table className="data-table">
          <thead>
            <tr>
              <th>Rule Name</th>
              <th>Severity</th>
              <th className="text-right">Incidents</th>
              <th>Last Incident</th>
            </tr>
          </thead>
          <tbody>
            {data.top20LeastIncidents.map((r) => (
              <tr key={r.ruleId}>
                <td>{r.ruleName}</td>
                <td>
                  <span className={`badge badge--${r.severity.toLowerCase()}`}>
                    {r.severity}
                  </span>
                </td>
                <td className="text-right">{data.incidentsLoaded ? r.incidentCount : data.incidentsError ? 'n/a' : '...'}</td>
                <td>{data.incidentsLoaded ? formatDate(r.lastIncident, 'Never') : '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
