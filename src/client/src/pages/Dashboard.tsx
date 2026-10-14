import { useEffect, useMemo, useState } from 'react';
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell,
} from 'recharts';
import {
  ShieldAlert, Database, Cable, AlertTriangle,
  CheckCircle, Activity, TrendingUp, ShieldCheck,
} from 'lucide-react';
import StatusCard from '../components/StatusCard';
import { useWorkspace } from '../auth/useWorkspace';
import { getAnalyticsRules, getIngestion, getDataConnectors, getDcr } from '../services/api';
import type { AnalyticsRulesResponse, IngestionResponse, DataConnectorsResponse, DcrResponse } from '../types';
import { useChartTheme } from '../hooks/useTheme';

const SEVERITY_COLORS: Record<string, string> = {
  High:          '#ef4444',
  Medium:        '#f59e0b',
  Low:           '#3b82f6',
  Informational: '#6b7280',
};

function RelativeTime({ iso }: { iso: string | null }) {
  // Capture the clock once per mount so rendering stays pure.
  const [now] = useState(Date.now);
  if (!iso) return <span style={{ color: 'var(--text-muted)' }}>-</span>;
  const diff = now - new Date(iso).getTime();
  const h = Math.floor(diff / 3_600_000);
  const d = Math.floor(h / 24);
  const label = d > 0 ? `${d}d ago` : h > 0 ? `${h}h ago` : 'just now';
  const color = d > 7 ? 'var(--danger)' : d > 1 ? 'var(--warning)' : 'var(--healthy)';
  return <span style={{ color, fontSize: '0.78rem' }}>{label}</span>;
}

function ConnectorDot({ status }: { status: string }) {
  const s = status.toLowerCase();
  const color = s === 'healthy' ? 'var(--healthy)' : s === 'warning' ? 'var(--warning)' : s === 'error' ? 'var(--danger)' : 'var(--text-muted)';
  return <span style={{ display: 'inline-block', width: 8, height: 8, borderRadius: '50%', background: color, boxShadow: `0 0 5px ${color}`, flexShrink: 0 }} />;
}

export default function Dashboard() {
  const { selectedWorkspace } = useWorkspace();
  const [analytics, setAnalytics] = useState<AnalyticsRulesResponse | null>(null);
  const [ingestion, setIngestion]   = useState<IngestionResponse | null>(null);
  const [connectors, setConnectors] = useState<DataConnectorsResponse | null>(null);
  const [dcr, setDcr]               = useState<DcrResponse | null>(null);
  const [loading, setLoading]       = useState(true);
  const chart = useChartTheme();

  useEffect(() => {
    if (!selectedWorkspace) return;
    const ws = selectedWorkspace;

    async function fetchAll() {
      try {
        const [a, i, c, d] = await Promise.allSettled([
          getAnalyticsRules(ws), getIngestion(ws), getDataConnectors(ws), getDcr(ws),
        ]);
        if (a.status === 'fulfilled') setAnalytics(a.value);
        if (i.status === 'fulfilled') setIngestion(i.value);
        if (c.status === 'fulfilled') setConnectors(c.value);
        if (d.status === 'fulfilled') setDcr(d.value);
      } finally {
        setLoading(false);
      }
    }
    setLoading(true);
    fetchAll();
    const interval = setInterval(fetchAll, 60_000);
    return () => clearInterval(interval);
  }, [selectedWorkspace]);

  // ── Derived data ──────────────────────────────────────────────────────────
  const allRules = useMemo(() => {
    if (!analytics) return [];
    const map = new Map<string, typeof analytics.top20MostIncidents[0]>();
    [...analytics.top20MostIncidents, ...analytics.top20LeastIncidents].forEach(r => map.set(r.ruleId, r));
    return Array.from(map.values());
  }, [analytics]);

  const severityData = useMemo(() => {
    const counts: Record<string, number> = {};
    allRules.forEach(r => { counts[r.severity] = (counts[r.severity] ?? 0) + 1; });
    return Object.entries(counts)
      .map(([name, value]) => ({ name, value }))
      .sort((a, b) => (SEVERITY_COLORS[a.name] ? 0 : 1) - (SEVERITY_COLORS[b.name] ? 0 : 1));
  }, [allRules]);

  const enabledCount  = useMemo(() => allRules.filter(r => r.enabled).length, [allRules]);
  const disabledCount = useMemo(() => allRules.filter(r => !r.enabled).length, [allRules]);
  const rulesWithIncidents = useMemo(() => allRules.filter(r => r.incidentCount > 0).length, [allRules]);

  const topCostTables = useMemo(() => {
    if (!ingestion) return [];
    return [...ingestion.tables]
      .sort((a, b) => b.estimatedCostUSD - a.estimatedCostUSD)
      .slice(0, 6)
      .map(t => ({
        name: t.tableName.length > 22 ? t.tableName.slice(0, 20) + '…' : t.tableName,
        cost: parseFloat(t.estimatedCostUSD.toFixed(2)),
        gb:   parseFloat(t.volumeGB.toFixed(2)),
      }));
  }, [ingestion]);

  if (loading) return <div className="loading">Loading dashboard…</div>;

  const connectorErrorCount = connectors?.errorCount ?? 0;
  const connectorWarnCount  = connectors?.warningCount ?? 0;
  const dcrErrors = dcr?.errorCount ?? 0;

  return (
    <div className="page">

      {/* ── Header ── */}
      <div className="page-header">
        <h2 className="page-title">Dashboard Overview</h2>
        {analytics?.lastRefreshed && (
          <span className="meta-text">
            Refreshed {new Date(analytics.lastRefreshed).toLocaleTimeString()}
          </span>
        )}
      </div>

      {/* ── KPI Cards ── */}
      <div className="cards-grid">
        <StatusCard
          title="Analytics Rules"
          value={analytics?.totalRules ?? '-'}
          subtitle={`${analytics?.totalIncidents ?? 0} incidents · ${enabledCount} enabled`}
          icon={<ShieldAlert size={24} />}
          color="blue"
        />
        <StatusCard
          title="Log Ingestion (30d)"
          value={`${ingestion?.totalVolumeGB?.toFixed(1) ?? '-'} GB`}
          subtitle={`Est. $${ingestion?.totalEstimatedCostUSD?.toFixed(2) ?? '-'} · ${ingestion?.tables?.length ?? 0} tables`}
          icon={<Database size={24} />}
          color="purple"
        />
        <StatusCard
          title="Connectors Healthy"
          value={`${connectors?.healthyCount ?? '-'} / ${connectors?.connectors?.length ?? '-'}`}
          subtitle={
            connectorErrorCount > 0 ? `${connectorErrorCount} error${connectorErrorCount > 1 ? 's' : ''}, ${connectorWarnCount} warning${connectorWarnCount !== 1 ? 's' : ''}`
            : connectorWarnCount > 0 ? `${connectorWarnCount} warning${connectorWarnCount > 1 ? 's' : ''}`
            : 'All connectors healthy'
          }
          icon={connectorErrorCount > 0 ? <AlertTriangle size={24} /> : <Cable size={24} />}
          color={connectorErrorCount > 0 ? 'red' : connectorWarnCount > 0 ? 'yellow' : 'green'}
        />
        <StatusCard
          title="DCR Status"
          value={`${dcr?.healthyCount ?? '-'} / ${dcr?.totalCount ?? '-'}`}
          subtitle={dcrErrors > 0 ? `${dcrErrors} error${dcrErrors > 1 ? 's' : ''}` : 'All receiving data'}
          icon={dcrErrors > 0 ? <AlertTriangle size={24} /> : <CheckCircle size={24} />}
          color={dcrErrors > 0 ? 'yellow' : 'green'}
        />
      </div>

      {/* ── Connector error banner ── */}
      {connectorErrorCount > 0 && connectors && (
        <div className="dash-alert-banner">
          <AlertTriangle size={16} />
          <span>
            <strong>{connectorErrorCount} connector{connectorErrorCount > 1 ? 's' : ''} with errors</strong>
            {': '}
            {connectors.connectors
              .filter(c => c.status === 'Error')
              .slice(0, 3)
              .map(c => c.connectorName)
              .join(', ')}
            {connectors.connectors.filter(c => c.status === 'Error').length > 3 && ' …'}
          </span>
        </div>
      )}

      {/* ── Main 2-col layout ── */}
      <div className="dash-grid">

        {/* ── Left column ── */}
        <div className="dash-col-main">

          {/* Top tables by cost: always visible after Phase 1 */}
          {topCostTables.length > 0 && (
            <div className="section">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.25rem' }}>
                <h3 style={{ margin: 0 }}>
                  Top Tables by Cost
                  <span style={{ color: 'var(--text-muted)', fontSize: '0.78rem', fontWeight: 400, marginLeft: '0.4rem' }}>(30 days)</span>
                </h3>
                <div style={{ display: 'flex', gap: '1.2rem', fontSize: '0.78rem', color: 'var(--text-muted)' }}>
                  <span>{ingestion?.tables?.length ?? 0} tables tracked</span>
                  <span>Total: <strong style={{ color: 'var(--text-primary)' }}>${ingestion?.totalEstimatedCostUSD?.toFixed(2)}</strong></span>
                </div>
              </div>
              <ResponsiveContainer width="100%" height={240}>
                <BarChart data={topCostTables} layout="vertical" margin={{ top: 0, right: 60, left: 10, bottom: 0 }}>
                  <XAxis type="number" stroke={chart.axisStroke} tick={{ fill: chart.axisTickFill, fontSize: 11 }} tickFormatter={v => `$${v}`} />
                  <YAxis type="category" dataKey="name" width={160} stroke={chart.axisStroke} tick={{ fill: chart.axisTickFill, fontSize: 11 }} />
                  <Tooltip
                    contentStyle={chart.tooltipStyle}
                    formatter={(v, _n, props) => [`$${v ?? 0} · ${props?.payload?.gb ?? 0} GB`, 'Est. Cost']}
                  />
                  <Bar dataKey="cost" fill="var(--accent)" radius={[0, 4, 4, 0]} maxBarSize={26} />
                </BarChart>
              </ResponsiveContainer>
            </div>
          )}

          {/* Top rules by incidents: available as soon as Phase 1 completes (partial data), full data after Phase 2 */}
          {analytics && (
            <div className="section">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.25rem' }}>
                <h3 style={{ margin: 0 }}>
                  Analytics Rules
                  <span style={{ color: 'var(--text-muted)', fontSize: '0.78rem', fontWeight: 400, marginLeft: '0.4rem' }}>(30 days)</span>
                </h3>
                <div style={{ display: 'flex', gap: '1rem', fontSize: '0.78rem', color: 'var(--text-muted)', alignItems: 'center' }}>
                  <span style={{ color: 'var(--healthy)' }}>● {enabledCount} enabled</span>
                  {disabledCount > 0 && <span style={{ color: 'var(--danger)' }}>● {disabledCount} disabled</span>}
                  {analytics.totalIncidents > 0
                    ? <span style={{ color: 'var(--warning)' }}>{rulesWithIncidents} firing</span>
                    : <span style={{ color: 'var(--text-muted)', fontStyle: 'italic' }}>loading incidents…</span>
                  }
                </div>
              </div>
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Rule Name</th>
                    <th>Severity</th>
                    <th style={{ textAlign: 'center' }}>Status</th>
                    <th style={{ textAlign: 'right' }}>Incidents</th>
                    <th>Last Incident</th>
                  </tr>
                </thead>
                <tbody>
                  {analytics.top20MostIncidents.slice(0, 8).map(r => (
                    <tr key={r.ruleId}>
                      <td style={{ fontWeight: 500, color: 'var(--text-primary)' }}>
                        <span className="truncate" title={r.ruleName} style={{ display: 'block', maxWidth: 340 }}>{r.ruleName}</span>
                      </td>
                      <td>
                        <span className={`badge badge--${r.severity.toLowerCase()}`}>{r.severity}</span>
                      </td>
                      <td style={{ textAlign: 'center' }}>
                        <span style={{
                          fontSize: '0.68rem', fontWeight: 700, padding: '0.15rem 0.5rem',
                          borderRadius: 6,
                          background: r.enabled ? 'rgba(0,209,84,0.12)' : 'rgba(255,77,77,0.1)',
                          color: r.enabled ? 'var(--healthy)' : 'var(--danger)',
                        }}>
                          {r.enabled ? 'ON' : 'OFF'}
                        </span>
                      </td>
                      <td style={{ textAlign: 'right', fontWeight: 700, color: r.incidentCount > 0 ? 'var(--warning)' : 'var(--text-muted)' }}>
                        {analytics.totalIncidents > 0 ? r.incidentCount : <span style={{ fontSize: '0.7rem', fontStyle: 'italic' }}>…</span>}
                      </td>
                      <td><RelativeTime iso={r.lastIncident} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>

        {/* ── Right column ── */}
        <div className="dash-col-side">

          {/* Connector status: TOP, most operational */}
          {connectors && connectors.connectors.length > 0 && (
            <div className="section">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
                <h3 style={{ margin: 0 }}>Connector Status</h3>
                <span style={{ fontSize: '0.75rem', color: connectors.errorCount > 0 ? 'var(--danger)' : connectors.warningCount > 0 ? 'var(--warning)' : 'var(--healthy)' }}>
                  {connectors.healthyCount}/{connectors.connectors.length} healthy
                </span>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.35rem' }}>
                {connectors.connectors.slice(0, 8).map(c => (
                  <div key={c.connectorId} style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', padding: '0.45rem 0.6rem', borderRadius: 10, background: 'var(--surface-sunken)', fontSize: '0.82rem' }}>
                    <ConnectorDot status={c.status} />
                    <span style={{ flex: 1, color: 'var(--text-primary)', fontWeight: 500, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}
                      title={c.connectorName}>{c.connectorName}</span>
                    {c.gapMinutes != null && c.gapMinutes > 60 ? (
                      <span style={{ fontSize: '0.7rem', color: 'var(--warning)', flexShrink: 0 }}>{Math.round(c.gapMinutes / 60)}h gap</span>
                    ) : (
                      <span style={{ fontSize: '0.7rem', color: 'var(--text-muted)', flexShrink: 0 }}>
                        {c.lastLogReceived ? <RelativeTime iso={c.lastLogReceived} /> : '-'}
                      </span>
                    )}
                  </div>
                ))}
                {connectors.connectors.length > 8 && (
                  <div style={{ textAlign: 'center', fontSize: '0.75rem', color: 'var(--text-muted)', padding: '0.25rem' }}>
                    +{connectors.connectors.length - 8} more
                  </div>
                )}
              </div>
            </div>
          )}

          {/* Quick stats */}
          <div className="section">
            <h3>Quick Stats</h3>
            <div style={{ display: 'flex', flexDirection: 'column', gap: '0.6rem' }}>
              {[
                { icon: <ShieldCheck size={14} />, label: 'Rules with incidents',  value: analytics ? (analytics.totalIncidents > 0 ? rulesWithIncidents : '…') : '-', color: rulesWithIncidents > 0 ? 'var(--warning)' : 'var(--healthy)' },
                { icon: <Activity     size={14} />, label: 'Total incidents (30d)', value: analytics?.totalIncidents ?? '-', color: (analytics?.totalIncidents ?? 0) > 0 ? 'var(--warning)' : 'var(--text-primary)' },
                { icon: <TrendingUp   size={14} />, label: 'Daily avg ingestion',   value: ingestion ? `${(ingestion.totalVolumeGB / (ingestion.daysAnalyzed || 30)).toFixed(2)} GB` : '-', color: 'var(--text-primary)' },
                { icon: <Database     size={14} />, label: 'Ingestion tables',      value: ingestion?.tables?.length ?? '-', color: 'var(--text-primary)' },
              ].map(s => (
                <div key={s.label} style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', fontSize: '0.82rem' }}>
                  <span style={{ color: 'var(--text-muted)', flexShrink: 0 }}>{s.icon}</span>
                  <span style={{ flex: 1, color: 'var(--text-secondary)' }}>{s.label}</span>
                  <span style={{ fontWeight: 700, color: s.color }}>{s.value}</span>
                </div>
              ))}
            </div>
          </div>

          {/* DCR quick summary */}
          {dcr && dcr.totalCount > 0 && (
            <div className="section">
              <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1rem' }}>
                <h3 style={{ margin: 0 }}>Data Collection Rules</h3>
                <span style={{ fontSize: '0.75rem', color: 'var(--text-muted)' }}>{dcr.totalCount} total</span>
              </div>
              <div style={{ display: 'flex', gap: '0.75rem' }}>
                {[
                  { label: 'Healthy', value: dcr.healthyCount, color: 'var(--healthy)', bg: 'rgba(0,209,84,0.1)' },
                  { label: 'Warning', value: dcr.warningCount, color: 'var(--warning)', bg: 'rgba(245,158,11,0.1)' },
                  { label: 'Error',   value: dcr.errorCount,   color: 'var(--danger)',  bg: 'rgba(255,77,77,0.1)' },
                ].map(s => (
                  <div key={s.label} style={{ flex: 1, textAlign: 'center', background: s.bg, borderRadius: 10, padding: '0.6rem 0.5rem' }}>
                    <div style={{ fontSize: '1.4rem', fontWeight: 700, color: s.color, letterSpacing: '-0.02em' }}>{s.value}</div>
                    <div style={{ fontSize: '0.68rem', color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', fontWeight: 600 }}>{s.label}</div>
                  </div>
                ))}
              </div>
              <div style={{ marginTop: '0.75rem', display: 'flex', flexDirection: 'column', gap: '0.35rem' }}>
                {dcr.dataCollectionRules.slice(0, 5).map(r => (
                  <div key={r.dcrId} style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', padding: '0.4rem 0.6rem', borderRadius: 8, background: 'var(--surface-sunken)', fontSize: '0.8rem' }}>
                    <span style={{
                      width: 6, height: 6, borderRadius: '50%', flexShrink: 0,
                      background: r.status === 'Healthy' ? 'var(--healthy)' : r.status === 'Warning' ? 'var(--warning)' : 'var(--danger)',
                    }} />
                    <span style={{ flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', color: 'var(--text-secondary)' }} title={r.dcrName}>{r.dcrName}</span>
                    <span style={{ fontSize: '0.7rem', color: 'var(--text-muted)', flexShrink: 0 }}>{r.destinations?.length ?? 0} dest</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Severity breakdown: only when incident data is loaded */}
          {severityData.length > 0 && analytics && analytics.totalIncidents > 0 && (
            <div className="section">
              <h3>Rule Severity Breakdown</h3>
              <div style={{ display: 'flex', alignItems: 'center', gap: '1rem' }}>
                <ResponsiveContainer width={120} height={120}>
                  <PieChart>
                    <Pie data={severityData} cx="50%" cy="50%" innerRadius={34} outerRadius={54} dataKey="value" paddingAngle={2}>
                      {severityData.map(entry => (
                        <Cell key={entry.name} fill={SEVERITY_COLORS[entry.name] ?? '#6b7280'} />
                      ))}
                    </Pie>
                    <Tooltip contentStyle={chart.tooltipStyle} />
                  </PieChart>
                </ResponsiveContainer>
                <div style={{ display: 'flex', flexDirection: 'column', gap: '0.4rem', flex: 1 }}>
                  {severityData.map(entry => (
                    <div key={entry.name} style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', fontSize: '0.82rem' }}>
                      <span style={{ width: 8, height: 8, borderRadius: '50%', background: SEVERITY_COLORS[entry.name] ?? '#6b7280', flexShrink: 0 }} />
                      <span style={{ flex: 1, color: 'var(--text-secondary)' }}>{entry.name}</span>
                      <span style={{ fontWeight: 700, color: 'var(--text-primary)' }}>{entry.value}</span>
                    </div>
                  ))}
                  <div style={{ borderTop: '1px solid var(--border)', paddingTop: '0.4rem', marginTop: '0.2rem', display: 'flex', justifyContent: 'space-between', fontSize: '0.78rem' }}>
                    <span style={{ color: 'var(--text-muted)' }}>Enabled</span>
                    <span style={{ color: 'var(--healthy)', fontWeight: 700 }}>
                      {Math.round((enabledCount / (enabledCount + disabledCount || 1)) * 100)}%
                    </span>
                  </div>
                </div>
              </div>
            </div>
          )}

        </div>
      </div>
    </div>
  );
}
