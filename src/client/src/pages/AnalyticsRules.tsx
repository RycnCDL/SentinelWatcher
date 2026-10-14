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
import { RefreshCw } from 'lucide-react';
import { useWorkspace } from '../auth/useWorkspace';
import { getAnalyticsRules, refreshAnalyticsRules } from '../services/api';
import { useChartTheme } from '../hooks/useTheme';
import type { AnalyticsRulesResponse } from '../types';

const SEVERITY_COLORS: Record<string, string> = {
  High: '#FF4D4D',
  Medium: '#F59E0B',
  Low: '#3B82F6',
  Informational: '#6B7280',
  Unknown: '#6B7280',
};

export default function AnalyticsRules() {
  const { selectedWorkspace } = useWorkspace();
  const { tooltipStyle, gridStroke, axisStroke, axisTickFill } = useChartTheme();
  const [data, setData] = useState<AnalyticsRulesResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);

  const fetchData = useCallback(async () => {
    if (!selectedWorkspace) return;
    try {
      const result = await getAnalyticsRules(selectedWorkspace);
      setData(result);
    } finally {
      setLoading(false);
    }
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
    setLoading(true);
    fetchData();
    const interval = setInterval(fetchData, 60000);
    return () => clearInterval(interval);
  }, [fetchData]);

  if (loading) return <div className="loading">Loading analytics rules...</div>;
  if (!data) return <div className="error">Failed to load data</div>;

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
            {data.totalRules} rules | {data.totalIncidents} incidents (30d) |
            Last refresh: {new Date(data.lastRefreshed).toLocaleTimeString()}
          </span>
          <button
            className="btn btn--icon"
            onClick={handleRefresh}
            disabled={refreshing}
          >
            <RefreshCw size={16} className={refreshing ? 'spinning' : ''} />
          </button>
        </div>
      </div>

      <div className="section">
        <h3>Top 20 Rules — Most Incidents</h3>
        <div className="chart-container">
          <ResponsiveContainer width="100%" height={500}>
            <BarChart data={topChartData} layout="vertical" margin={{ left: 200 }}>
              <CartesianGrid strokeDasharray="3 3" stroke={gridStroke} />
              <XAxis type="number" stroke={axisStroke} tick={{ fill: axisStroke, fontSize: 12 }} />
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
              <th>Incidents</th>
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
                <td>
                  {r.lastIncident
                    ? new Date(r.lastIncident).toLocaleString()
                    : '-'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="section">
        <h3>Top 20 Rules — Least / No Incidents (Enabled)</h3>
        <table className="data-table">
          <thead>
            <tr>
              <th>Rule Name</th>
              <th>Severity</th>
              <th>Incidents</th>
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
                <td className="text-right">{r.incidentCount}</td>
                <td>
                  {r.lastIncident
                    ? new Date(r.lastIncident).toLocaleString()
                    : 'Never'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
