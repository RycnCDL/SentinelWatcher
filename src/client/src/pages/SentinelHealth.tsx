import { useEffect, useState } from 'react';
import {
  AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell, Legend,
} from 'recharts';
import { AlertTriangle, HeartPulse, ShieldOff, Zap, Wifi } from 'lucide-react';
import { useWorkspace } from '../auth/useWorkspace';
import { getSentinelHealth } from '../services/api';
import type {
  SentinelHealthResponse,
  ConnectorCurrentStatus,
  HealthFailureEvent,
  HealthTrendPoint,
  LoadError,
} from '../types';
import { LoadErrorState } from '../components/LoadErrorState';
import { loadErrorOf } from '../services/loadError';
import { useChartTheme } from '../hooks/useTheme';

// ─── Sub-components ───────────────────────────────────────────────────────────

function ScoreRing({ score }: { score: number }) {
  const r = 52;
  const circ = 2 * Math.PI * r;
  const fill = circ * (Math.min(score, 100) / 100);
  const color = score >= 80 ? '#00D154' : score >= 50 ? '#F59E0B' : '#FF4D4D';
  const isFull = score >= 100;

  return (
    <div className="sh-score-ring">
      <svg width="140" height="140" viewBox="0 0 140 140">
        {/* Track */}
        <circle cx="70" cy="70" r={r} fill="none" stroke="rgba(255,255,255,0.06)" strokeWidth="10" />
        {/* Fill: solid circle when 100%, dashed arc otherwise */}
        {isFull ? (
          <circle
            cx="70" cy="70" r={r}
            fill="none"
            stroke={color}
            strokeWidth="10"
            style={{ filter: `drop-shadow(0 0 8px ${color})` }}
          />
        ) : (
          <circle
            cx="70" cy="70" r={r}
            fill="none"
            stroke={color}
            strokeWidth="10"
            strokeLinecap="round"
            strokeDasharray={`${fill} ${circ}`}
            strokeDashoffset={circ / 4}
            style={{ filter: `drop-shadow(0 0 8px ${color})`, transition: 'stroke-dasharray 0.8s ease' }}
          />
        )}
        <text x="70" y="64" textAnchor="middle" fill={color} fontSize="28" fontWeight="700">{score}</text>
        <text x="70" y="82" textAnchor="middle" fill="rgba(160,170,181,0.8)" fontSize="11">Health Score</text>
      </svg>
    </div>
  );
}

// SentinelResourceType values are "Data connector", "Analytics rule" and so on.
function typeKey(type: string): string {
  return type.replace(/\s+/g, '').toLowerCase();
}

function TypeChip({ type }: { type: string }) {
  const map: Record<string, { bg: string; color: string }> = {
    dataconnector:  { bg: 'rgba(59,130,246,0.15)',  color: '#93c5fd' },
    analyticsrule:  { bg: 'rgba(168,85,247,0.15)',  color: '#d8b4fe' },
    automationrule: { bg: 'rgba(245,158,11,0.15)',  color: '#fcd34d' },
    playbook:       { bg: 'rgba(20,184,166,0.15)',  color: '#5eead4' },
  };
  const style = map[typeKey(type)] ?? { bg: 'rgba(107,114,128,0.12)', color: '#9ca3af' };
  return (
    <span className="sh-type-chip" style={{ background: style.bg, color: style.color }}>
      {type}
    </span>
  );
}

function StatusDot({ status }: { status: string }) {
  const s = status.toLowerCase();
  const color = s === 'success' || s === 'healthy' || s === 'running'
    ? '#00D154'
    : s === 'failure' || s === 'failed' || s === 'error'
    ? '#FF4D4D'
    : '#F59E0B';
  return (
    <span
      className="sh-status-dot"
      style={{ background: color, boxShadow: `0 0 6px ${color}` }}
      title={status}
    />
  );
}

function isHealthy(c: ConnectorCurrentStatus): boolean {
  return !c.isStale && ['success', 'healthy', 'running'].includes(c.status.toLowerCase());
}

function ConnectorRow({ c, days }: { c: ConnectorCurrentStatus; days: number }) {
  const isOk = isHealthy(c);
  return (
    <tr>
      <td>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
          <StatusDot status={c.status} />
          <span style={{ color: 'var(--text-primary)', fontWeight: 500 }}>{c.name}</span>
        </div>
      </td>
      <td><TypeChip type={c.kind} /></td>
      <td>
        {c.isStale ? (
          <span
            style={{ color: 'var(--text-muted)', fontWeight: 600, fontSize: '0.82rem' }}
            title={`No health event in the last ${days} days. Last reported status: ${c.status}`}
          >
            Stale ({c.status})
          </span>
        ) : (
          <span style={{ color: isOk ? 'var(--healthy)' : 'var(--danger)', fontWeight: 600, fontSize: '0.82rem' }}>
            {c.status}
          </span>
        )}
      </td>
      <td style={{ color: 'var(--text-muted)', fontSize: '0.8rem' }}>
        {c.lastEvent ? new Date(c.lastEvent).toLocaleString() : '-'}
      </td>
      <td style={{ color: 'var(--text-secondary)', fontSize: '0.8rem', maxWidth: 260 }}>
        {c.category ?? '-'}
      </td>
    </tr>
  );
}

function FailureRow({ f }: { f: HealthFailureEvent }) {
  return (
    <tr>
      <td className="nowrap" style={{ color: 'var(--text-muted)', fontSize: '0.8rem' }}>
        {new Date(f.timeGenerated).toLocaleString()}
      </td>
      <td><TypeChip type={f.resourceType} /></td>
      <td style={{ color: 'var(--text-primary)', fontWeight: 500, fontSize: '0.85rem' }}>
        <span className="truncate" style={{ maxWidth: 220, display: 'block' }} title={f.resourceName}>
          {f.resourceName}
        </span>
      </td>
      <td style={{ color: 'var(--danger)', fontWeight: 600, fontSize: '0.82rem' }}>{f.status}</td>
      <td style={{ color: 'var(--text-secondary)', fontSize: '0.8rem' }}>{f.category}</td>
    </tr>
  );
}

// ─── Pie colours ──────────────────────────────────────────────────────────────
const PIE_COLORS: Record<string, string> = {
  dataconnector:  '#3B82F6',
  analyticsrule:  '#a855f7',
  automationrule: '#F59E0B',
  playbook:       '#14b8a6',
  other:          '#6B7280',
};

// ─── Main page ────────────────────────────────────────────────────────────────
export default function SentinelHealth() {
  const { selectedWorkspace } = useWorkspace();
  const [data, setData] = useState<SentinelHealthResponse | null>(null);
  const [days, setDays] = useState(7);
  // Key of the last request that finished (successfully or not). Loading is derived
  // from it instead of being set synchronously inside the effect.
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const [connectorFilter, setConnectorFilter] = useState<'all' | 'issues'>('all');
  const [error, setError] = useState<LoadError | null>(null);
  const chart = useChartTheme();

  const requestKey = selectedWorkspace ? `${selectedWorkspace.resourceId}|${days}` : null;
  const loading = requestKey === null || settledKey !== requestKey;

  useEffect(() => {
    if (!selectedWorkspace || requestKey === null) return;
    getSentinelHealth(selectedWorkspace, days)
      .then((result) => {
        setData(result);
        setError(null);
      })
      .catch((err: unknown) => {
        // A failure is an error, never "health monitoring not enabled".
        setData(null);
        setError(loadErrorOf(err));
      })
      .finally(() => setSettledKey(requestKey));
  }, [selectedWorkspace, days, requestKey]);

  if (loading) return <div className="loading">Loading Sentinel Health…</div>;

  if (!data) {
    return (
      <div className="page">
        <div className="page-header">
          <h2 className="page-title">Sentinel Health</h2>
        </div>
        {error && <LoadErrorState error={error} what="Sentinel Health" />}
      </div>
    );
  }

  // ── Not enabled ─────────────────────────────────────────────────────────────
  if (!data.healthMonitoringEnabled) {
    return (
      <div className="page">
        <div className="page-header">
          <h2 className="page-title">Sentinel Health</h2>
        </div>
        <div className="section" style={{ textAlign: 'center', padding: '4rem 2rem' }}>
          <HeartPulse size={48} style={{ color: 'var(--text-muted)', margin: '0 auto 1rem' }} />
          <h3 style={{ color: 'var(--text-primary)', marginBottom: '0.5rem' }}>Health Monitoring Not Enabled</h3>
          <p style={{ color: 'var(--text-muted)', maxWidth: 520, margin: '0 auto 1.5rem', lineHeight: 1.7 }}>
            The <code style={{ color: 'var(--accent)' }}>_SentinelHealth()</code> table is empty or not available
            in this workspace. Enable{' '}
            <strong>Microsoft Sentinel → Settings → Diagnostic settings</strong> and turn on
            health monitoring for Connectors, Rules and Automation.
          </p>
          <span className="sh-type-chip" style={{ background: 'rgba(245,158,11,0.15)', color: '#fcd34d', fontSize: '0.85rem' }}>
            ⚠ No data from _SentinelHealth()
          </span>
        </div>
      </div>
    );
  }

  // ─── Derived data ──────────────────────────────────────────────────────────
  const failuresByType = (() => {
    const counts: Record<string, number> = {};
    data.recentFailures.forEach(f => {
      counts[f.resourceType] = (counts[f.resourceType] ?? 0) + 1;
    });
    return Object.entries(counts).map(([name, value]) => ({ name, value }));
  })();

  const connectorIssues = data.connectorStatuses.filter(c => !isHealthy(c));
  const filteredConnectors = connectorFilter === 'issues' ? connectorIssues : data.connectorStatuses;

  // The API sends ISO dates (yyyy-MM-dd); parse them as local dates for display.
  const trendFormatted: HealthTrendPoint[] = data.trend.map(t => ({
    ...t,
    day: new Date(`${t.day}T00:00:00`).toLocaleDateString(undefined, { month: 'short', day: 'numeric' }),
  }));

  const connectorHealthPct = data.totalConnectors > 0
    ? Math.round((data.healthyConnectors / data.totalConnectors) * 100)
    : 100;

  const ruleFailurePct = data.totalRuleRuns > 0
    ? Math.round((data.failedRuleRuns / data.totalRuleRuns) * 100)
    : 0;

  // ─── Render ────────────────────────────────────────────────────────────────
  return (
    <div className="page">

      {/* ── Header ── */}
      <div className="page-header">
        <h2 className="page-title">Sentinel Health</h2>
        <div className="page-controls">
          <select value={days} onChange={e => setDays(Number(e.target.value))} className="control-select">
            <option value={1}>Last 24 hours</option>
            <option value={3}>Last 3 days</option>
            <option value={7}>Last 7 days</option>
            <option value={14}>Last 14 days</option>
          </select>
        </div>
      </div>

      {/* ── Auto-disabled rules banner ── */}
      {data.autoDisabledRulesCount > 0 && (
        <div className="sh-autodisabled-banner">
          <ShieldOff size={18} />
          <span>
            <strong>{data.autoDisabledRulesCount} analytics rule{data.autoDisabledRulesCount > 1 ? 's' : ''} auto-disabled</strong>
            {'. '}Consecutive failures caused Sentinel to disable them automatically.
          </span>
          <div className="sh-autodisabled-list">
            {data.autoDisabledRules.map((r, i) => (
              <span key={i} className="sh-autodisabled-rule" title={r.resourceId}>
                {r.name}
                <span className="sh-autodisabled-date">{new Date(r.lastSeen).toLocaleDateString()}</span>
              </span>
            ))}
          </div>
        </div>
      )}

      {/* ── Score + KPIs row ── */}
      <div className="sh-hero-row">
        <div className="section sh-score-section">
          <ScoreRing score={data.healthScore} />
          <div className="sh-score-label">
            <span style={{ color: 'var(--text-muted)', fontSize: '0.72rem', textTransform: 'uppercase', letterSpacing: '0.07em' }}>
              Overall Health
            </span>
            <span style={{ color: 'var(--text-secondary)', fontSize: '0.8rem', marginTop: '0.25rem' }}>
              as of {new Date(data.queryTime).toLocaleTimeString()}
            </span>
          </div>
        </div>

        <div className="sh-kpi-grid">
          <div className="section sh-kpi-card sh-kpi-card--connector">
            <div className="sh-kpi-icon"><Wifi size={20} /></div>
            <div className="sh-kpi-body">
              <div className="sh-kpi-value">{data.healthyConnectors}<span className="sh-kpi-total">/{data.totalConnectors}</span></div>
              <div className="sh-kpi-label">Connector health</div>
              <div className="sh-kpi-sub" style={{ color: connectorHealthPct < 80 ? 'var(--danger)' : 'var(--healthy)' }}>
                {connectorHealthPct}% healthy
                {data.staleConnectors > 0 && `, ${data.staleConnectors} stale`}
              </div>
            </div>
          </div>

          <div className="section sh-kpi-card sh-kpi-card--rules">
            <div className="sh-kpi-icon"><Zap size={20} /></div>
            <div className="sh-kpi-body">
              <div className="sh-kpi-value">{data.failedRuleRuns}<span className="sh-kpi-total">/{data.totalRuleRuns}</span></div>
              <div className="sh-kpi-label">Rule Runs Failed</div>
              <div className="sh-kpi-sub" style={{ color: ruleFailurePct > 5 ? 'var(--danger)' : 'var(--healthy)' }}>
                {ruleFailurePct}% failure rate
              </div>
            </div>
          </div>

          <div className="section sh-kpi-card sh-kpi-card--disabled">
            <div className="sh-kpi-icon"><ShieldOff size={20} /></div>
            <div className="sh-kpi-body">
              <div className="sh-kpi-value" style={{ color: data.autoDisabledRulesCount > 0 ? 'var(--danger)' : 'var(--healthy)' }}>
                {data.autoDisabledRulesCount}
              </div>
              <div className="sh-kpi-label">Auto-Disabled Rules</div>
              <div className="sh-kpi-sub" style={{ color: data.autoDisabledRulesCount > 0 ? 'var(--danger)' : 'var(--text-muted)' }}>
                {data.autoDisabledRulesCount > 0 ? 'Needs attention' : 'All rules active'}
              </div>
            </div>
          </div>

          <div className="section sh-kpi-card sh-kpi-card--automation">
            <div className="sh-kpi-icon"><AlertTriangle size={20} /></div>
            <div className="sh-kpi-body">
              <div className="sh-kpi-value">{data.failedAutomationRuns}<span className="sh-kpi-total">/{data.totalAutomationRuns}</span></div>
              <div className="sh-kpi-label">Automation Failures</div>
              <div className="sh-kpi-sub" style={{ color: data.failedAutomationRuns > 0 ? 'var(--warning)' : 'var(--healthy)' }}>
                {data.totalAutomationRuns > 0
                  ? `${Math.round((data.failedAutomationRuns / data.totalAutomationRuns) * 100)}% failure rate`
                  : 'No runs recorded'}
              </div>
            </div>
          </div>
        </div>
      </div>

      {/* ── Charts row ── */}
      <div className="charts-row">

        {/* 7-day trend */}
        <div className="section chart-half">
          <h3>Failure Trend ({days}d)</h3>
          <ResponsiveContainer width="100%" height={220}>
            <AreaChart data={trendFormatted} margin={{ top: 5, right: 16, left: -10, bottom: 0 }}>
              <defs>
                <linearGradient id="gradConn" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#3B82F6" stopOpacity={0.35} />
                  <stop offset="95%" stopColor="#3B82F6" stopOpacity={0} />
                </linearGradient>
                <linearGradient id="gradRule" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#a855f7" stopOpacity={0.35} />
                  <stop offset="95%" stopColor="#a855f7" stopOpacity={0} />
                </linearGradient>
                <linearGradient id="gradAuto" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="5%" stopColor="#F59E0B" stopOpacity={0.35} />
                  <stop offset="95%" stopColor="#F59E0B" stopOpacity={0} />
                </linearGradient>
              </defs>
              <CartesianGrid strokeDasharray="3 3" stroke={chart.gridStroke} />
              <XAxis dataKey="day" stroke={chart.axisStroke} tick={{ fill: chart.axisTickFill, fontSize: 11 }} />
              <YAxis stroke={chart.axisStroke} tick={{ fill: chart.axisTickFill, fontSize: 11 }} allowDecimals={false} />
              <Tooltip contentStyle={chart.tooltipStyle} />
              <Legend wrapperStyle={{ fontSize: '0.78rem', color: chart.axisTickFill }} />
              <Area type="monotone" dataKey="dataConnectorFailures" name="Connectors" stroke="#3B82F6" fill="url(#gradConn)" strokeWidth={2} dot={false} />
              <Area type="monotone" dataKey="analyticsRuleFailures" name="Rules" stroke="#a855f7" fill="url(#gradRule)" strokeWidth={2} dot={false} />
              <Area type="monotone" dataKey="automationFailures" name="Automation" stroke="#F59E0B" fill="url(#gradAuto)" strokeWidth={2} dot={false} />
            </AreaChart>
          </ResponsiveContainer>
        </div>

        {/* Failure distribution donut */}
        <div className="section chart-half">
          <h3>Failure Distribution (24h)</h3>
          {failuresByType.length === 0 ? (
            <div style={{ height: 220, display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--healthy)', fontSize: '0.9rem', gap: '0.5rem' }}>
              <HeartPulse size={20} /> No failures in the last 24 hours
            </div>
          ) : (
            <ResponsiveContainer width="100%" height={220}>
              <PieChart>
                <Pie
                  data={failuresByType}
                  cx="50%" cy="50%"
                  innerRadius={55} outerRadius={80}
                  paddingAngle={3}
                  dataKey="value"
                >
                  {failuresByType.map((entry) => (
                    <Cell key={entry.name} fill={PIE_COLORS[typeKey(entry.name)] ?? PIE_COLORS.other} />
                  ))}
                </Pie>
                <Tooltip contentStyle={chart.tooltipStyle} />
                <Legend
                  iconType="circle"
                  iconSize={8}
                  wrapperStyle={{ fontSize: '0.78rem', color: chart.axisTickFill }}
                />
              </PieChart>
            </ResponsiveContainer>
          )}
        </div>
      </div>

      {/* ── Connector Status table ── */}
      <div className="section">
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '1.25rem' }}>
          <h3 style={{ margin: 0 }} title="Latest status per connector reported by SentinelHealth. Stale: no health event in the selected range.">
            Connector health (SentinelHealth)
          </h3>
          <div className="filter-bar" style={{ margin: 0, padding: 0 }}>
            <button className={`filter-btn ${connectorFilter === 'all' ? 'active' : ''}`} onClick={() => setConnectorFilter('all')}>
              All ({data.connectorStatuses.length})
            </button>
            <button className={`filter-btn ${connectorFilter === 'issues' ? 'active' : ''}`} onClick={() => setConnectorFilter('issues')}>
              Issues and stale ({connectorIssues.length})
            </button>
          </div>
        </div>
        {filteredConnectors.length === 0 ? (
          <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--healthy)' }}>
            ✓ All connectors healthy
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Connector</th>
                <th>Type</th>
                <th>Status</th>
                <th>Last Event</th>
                <th>Category</th>
              </tr>
            </thead>
            <tbody>
              {filteredConnectors.map((c, i) => <ConnectorRow key={i} c={c} days={days} />)}
            </tbody>
          </table>
        )}
      </div>

      {/* ── Recent Failures ── */}
      <div className="section">
        <h3>Recent Failures (24h)</h3>
        {data.recentFailures.length === 0 ? (
          <div style={{ textAlign: 'center', padding: '2rem', color: 'var(--healthy)' }}>
            ✓ No failures in the last 24 hours
          </div>
        ) : (
          <table className="data-table">
            <thead>
              <tr>
                <th>Time</th>
                <th>Resource Type</th>
                <th>Name</th>
                <th>Status</th>
                <th>Category</th>
              </tr>
            </thead>
            <tbody>
              {data.recentFailures.map((f, i) => <FailureRow key={i} f={f} />)}
            </tbody>
          </table>
        )}
      </div>

    </div>
  );
}
