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
import type { AnalyticsRulesResponse, IngestionResponse, DataConnectorsResponse, DcrResponse, LoadError } from '../types';
import { LOAD_ERROR_TITLE, loadErrorOf } from '../services/loadError';
import { useChartTheme } from '../hooks/useTheme';
import { isDcrPageEnabled } from '../config/runtimeConfig';
import { formatGb, formatUsd } from '../utils/format';

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

type Section = 'analytics' | 'ingestion' | 'connectors' | 'dcr';

const NO_ERRORS: Record<Section, LoadError | null> = { analytics: null, ingestion: null, connectors: null, dcr: null };

const SECTION_LABELS: Record<Section, string> = {
  analytics: 'Analytics rules',
  ingestion: 'Log ingestion',
  connectors: 'Data freshness',
  dcr: 'Data collection rules',
};

// Value, subtitle and color of a KPI card whose data is missing: an error or still loading.
function missing(error: LoadError | null) {
  return error
    ? { value: 'Error', subtitle: LOAD_ERROR_TITLE, color: 'red' as const }
    : { value: '…', subtitle: 'Loading…', color: 'blue' as const };
}

export default function Dashboard() {
  const { selectedWorkspace } = useWorkspace();
  const [analytics, setAnalytics] = useState<AnalyticsRulesResponse | null>(null);
  const [ingestion, setIngestion]   = useState<IngestionResponse | null>(null);
  const [connectors, setConnectors] = useState<DataConnectorsResponse | null>(null);
  const [dcr, setDcr]               = useState<DcrResponse | null>(null);
  const [errors, setErrors] = useState<Record<Section, LoadError | null>>(NO_ERRORS);
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const loading = !selectedWorkspace || settledKey !== selectedWorkspace.resourceId;
  const chart = useChartTheme();
  // Read at render time: the runtime config is loaded after this module is imported.
  const showDcr = isDcrPageEnabled();

  useEffect(() => {
    if (!selectedWorkspace) return;
    const ws = selectedWorkspace;

    async function fetchAll() {
      try {
        const [a, i, c, d] = await Promise.allSettled([
          getAnalyticsRules(ws), getIngestion(ws), getDataConnectors(ws),
          showDcr ? getDcr(ws) : Promise.reject(new Error('DCR page disabled')),
        ]);
        // A failed section shows its error, never empty data (which would read as zeros or "all good").
        setAnalytics(a.status === 'fulfilled' ? a.value : null);
        setIngestion(i.status === 'fulfilled' ? i.value : null);
        setConnectors(c.status === 'fulfilled' ? c.value : null);
        setDcr(d.status === 'fulfilled' ? d.value : null);
        setErrors({
          analytics: a.status === 'rejected' ? loadErrorOf(a.reason) : null,
          ingestion: i.status === 'rejected' ? loadErrorOf(i.reason) : null,
          connectors: c.status === 'rejected' ? loadErrorOf(c.reason) : null,
          dcr: showDcr && d.status === 'rejected' ? loadErrorOf(d.reason) : null,
        });
      } finally {
        setSettledKey(ws.resourceId);
      }
    }
    fetchAll();
    const interval = setInterval(fetchAll, 60_000);
    return () => clearInterval(interval);
  }, [selectedWorkspace, showDcr]);

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
  const dcrWarnings = dcr?.warningCount ?? 0;
  const failedSections = (Object.keys(errors) as Section[]).filter(k => errors[k] && errors[k]!.code !== 'Loading');
  const staleSections = [
    analytics?.refreshError && SECTION_LABELS.analytics,
    ingestion?.refreshError && SECTION_LABELS.ingestion,
    connectors?.refreshError && SECTION_LABELS.connectors,
    dcr?.refreshError && SECTION_LABELS.dcr,
  ].filter(Boolean);

  const incidentsText = !analytics ? '-'
    : analytics.incidentsLoaded ? String(analytics.totalIncidents)
    : analytics.incidentsError ? 'n/a' : '…';

  const analyticsCard = analytics
    ? {
        value: analytics.totalRules,
        subtitle: analytics.incidentsLoaded
          ? `${analytics.totalIncidents} incidents · ${enabledCount} enabled`
          : analytics.incidentsError
            ? `Incident counts unavailable · ${enabledCount} enabled`
            : `Loading incidents · ${enabledCount} enabled`,
        color: 'blue' as const,
      }
    : missing(errors.analytics);

  const ingestionCard = ingestion
    ? {
        value: formatGb(ingestion.totalVolumeGB, 1),
        subtitle: `Est. ${formatUsd(ingestion.totalEstimatedCostUSD)} · ${ingestion.tables.length} tables`,
        color: 'purple' as const,
      }
    : missing(errors.ingestion);

  // "All ... receiving data" only when the data is complete and from the latest refresh.
  const connectorsCard = connectors
    ? {
        value: `${connectors.healthyCount} / ${connectors.connectors.length}`,
        subtitle:
          connectorErrorCount > 0 ? `${connectorErrorCount} error${connectorErrorCount > 1 ? 's' : ''}, ${connectorWarnCount} warning${connectorWarnCount !== 1 ? 's' : ''}`
          : connectorWarnCount > 0 ? `${connectorWarnCount} warning${connectorWarnCount > 1 ? 's' : ''}`
          : connectors.partialError ? 'Connector list may be incomplete'
          : connectors.refreshError ? 'Latest refresh failed, older data'
          : connectors.connectors.length === 0 ? 'No connectors detected'
          : connectors.quietCount > 0 ? `Receiving data, ${connectors.quietCount} quiet`
          : 'All connectors receiving data',
        color: connectorErrorCount > 0 ? 'red' as const
          : connectorWarnCount > 0 || connectors.partialError || connectors.refreshError ? 'yellow' as const
          : 'green' as const,
      }
    : missing(errors.connectors);

  const dcrCard = dcr
    ? {
        value: `${dcr.healthyCount} / ${dcr.totalCount}`,
        subtitle:
          dcrErrors > 0 ? `${dcrErrors} error${dcrErrors > 1 ? 's' : ''}`
          : dcrWarnings > 0 ? `${dcrWarnings} warning${dcrWarnings > 1 ? 's' : ''}`
          : dcr.refreshError ? 'Latest refresh failed, older data'
          : dcr.totalCount === 0 ? 'No DCRs in the workspace resource group'
          : dcr.healthyCount < dcr.totalCount ? `${dcr.totalCount - dcr.healthyCount} not checked`
          : 'All receiving data',
        color: dcrErrors > 0 || dcrWarnings > 0 || dcr.refreshError ? 'yellow' as const : 'green' as const,
      }
    : missing(errors.dcr);

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

      {/* ── Load errors ── */}
      {failedSections.length > 0 && (
        <div className="alert alert--error" role="alert">
          <AlertTriangle size={14} />
          <span>
            <strong>{LOAD_ERROR_TITLE}.</strong>{' '}
            Failed: {failedSections.map(k => `${SECTION_LABELS[k]} (${errors[k]!.code})`).join(', ')}.
          </span>
        </div>
      )}
      {staleSections.length > 0 && (
        <div className="alert alert--warning" role="status">
          <AlertTriangle size={14} />
          <span>The latest refresh failed for {staleSections.join(', ')}. Showing older data.</span>
        </div>
      )}

      {/* ── KPI Cards ── */}
      <div className="cards-grid">
        <StatusCard
          title="Analytics Rules"
          value={analyticsCard.value}
          subtitle={analyticsCard.subtitle}
          icon={analytics ? <ShieldAlert size={24} /> : errors.analytics ? <AlertTriangle size={24} /> : <ShieldAlert size={24} />}
          color={analyticsCard.color}
        />
        <StatusCard
          title="Log Ingestion (30d)"
          value={ingestionCard.value}
          subtitle={ingestionCard.subtitle}
          icon={!ingestion && errors.ingestion ? <AlertTriangle size={24} /> : <Database size={24} />}
          color={ingestionCard.color}
        />
        <StatusCard
          title="Data freshness"
          value={connectorsCard.value}
          subtitle={connectorsCard.subtitle}
          icon={connectorErrorCount > 0 || (!connectors && errors.connectors) ? <AlertTriangle size={24} /> : <Cable size={24} />}
          color={connectorsCard.color}
        />
        {showDcr && (
          <StatusCard
            title="DCR Status"
            value={dcrCard.value}
            subtitle={dcrCard.subtitle}
            icon={dcrErrors > 0 || (!dcr && errors.dcr) ? <AlertTriangle size={24} /> : <CheckCircle size={24} />}
            color={dcrCard.color}
          />
        )}
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
                  <span>Total: <strong style={{ color: 'var(--text-primary)' }}>{formatUsd(ingestion?.totalEstimatedCostUSD)}</strong></span>
                </div>
              </div>
              <ResponsiveContainer width="100%" height={240}>
                <BarChart data={topCostTables} layout="vertical" margin={{ top: 0, right: 60, left: 10, bottom: 0 }}>
                  <XAxis type="number" stroke={chart.axisStroke} tick={{ fill: chart.axisTickFill, fontSize: 11 }} tickFormatter={v => formatUsd(Number(v))} />
                  <YAxis type="category" dataKey="name" width={160} stroke={chart.axisStroke} tick={{ fill: chart.axisTickFill, fontSize: 11 }} />
                  <Tooltip
                    contentStyle={chart.tooltipStyle}
                    formatter={(v, _n, props) => [`${formatUsd(Number(v ?? 0))} · ${formatGb(Number(props?.payload?.gb ?? 0))}`, 'Est. Cost']}
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
                  {analytics.incidentsLoaded
                    ? <span style={{ color: 'var(--warning)' }}>{rulesWithIncidents} firing</span>
                    : analytics.incidentsError
                    ? <span style={{ color: 'var(--danger)' }}>incident counts unavailable</span>
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
                        {analytics.incidentsLoaded ? r.incidentCount : <span style={{ fontSize: '0.7rem', fontStyle: 'italic' }}>{analytics.incidentsError ? 'n/a' : '…'}</span>}
                      </td>
                      <td><RelativeTime iso={r.lastIncident} /></td>
                    </tr>
                  ))}
                  {analytics.incidentsLoaded && analytics.top20MostIncidents.length === 0 && (
                    <tr>
                      <td colSpan={5} style={{ color: 'var(--text-muted)', textAlign: 'center' }}>
                        No incidents from existing analytics rules in the last 30 days
                        {analytics.totalIncidents > 0 && ` (${analytics.totalIncidents} incidents from other sources, see Analytics Rules)`}
                      </td>
                    </tr>
                  )}
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
                <h3 style={{ margin: 0 }} title="Based on the newest record in each connector's tables">Data freshness</h3>
                <span style={{ fontSize: '0.75rem', color: connectors.errorCount > 0 ? 'var(--danger)' : connectors.warningCount > 0 ? 'var(--warning)' : 'var(--healthy)' }}>
                  {connectors.healthyCount}/{connectors.connectors.length} receiving data
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
                { icon: <ShieldCheck size={14} />, label: 'Rules with incidents',  value: analytics ? (analytics.incidentsLoaded ? rulesWithIncidents : analytics.incidentsError ? 'n/a' : '…') : '-', color: rulesWithIncidents > 0 ? 'var(--warning)' : 'var(--healthy)' },
                { icon: <Activity     size={14} />, label: 'Total incidents (30d)', value: incidentsText, color: analytics?.incidentsLoaded && analytics.totalIncidents > 0 ? 'var(--warning)' : 'var(--text-primary)' },
                { icon: <TrendingUp   size={14} />, label: 'Daily avg ingestion',   value: ingestion ? formatGb(ingestion.totalVolumeGB / (ingestion.daysAnalyzed || 30)) : '-', color: 'var(--text-primary)' },
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
                      background: r.status === 'Healthy' ? 'var(--healthy)' : r.status === 'Warning' ? 'var(--warning)' : r.status === 'Error' ? 'var(--danger)' : 'var(--text-muted)',
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
