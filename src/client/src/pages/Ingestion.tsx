import { useEffect, useState, useMemo } from 'react';
import { useChartTheme } from '../hooks/useTheme';
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  PieChart,
  Pie,
  Cell,
} from 'recharts';
import { useWorkspace } from '../auth/WorkspaceContext';
import { getIngestion, getDataConnectors } from '../services/api';
import type { IngestionResponse, DataConnectorsResponse } from '../types';

const COLORS = [
  '#3B82F6', '#00D154', '#F59E0B', '#FF4D4D', '#a855f7',
  '#06b6d4', '#ec4899', '#6366f1', '#d946ef', '#f97316',
  '#14b8a6', '#8b5cf6', '#e11d48', '#84cc16', '#0ea5e9',
  '#7c3aed', '#db2777', '#059669', '#dc2626', '#ca8a04',
];


// Only show label for large enough slices
function renderLabel({ name, percent }: { name?: string; percent?: number }) {
  if ((percent ?? 0) < 0.05) return null;
  return `${name} (${((percent ?? 0) * 100).toFixed(1)}%)`;
}

// Custom compact legend
function CustomLegend({ data, colors }: { data: { name: string; value: number }[]; colors: string[] }) {
  return (
    <div className="pie-legend">
      {data.map((entry, index) => (
        <div key={entry.name} className="pie-legend__item">
          <span className="pie-legend__dot" style={{ background: colors[index % colors.length] }} />
          <span className="pie-legend__name">{entry.name}</span>
          <span className="pie-legend__value">{entry.value.toFixed(2)} GB</span>
        </div>
      ))}
    </div>
  );
}

export default function Ingestion() {
  const { tooltipStyle, gridStroke, axisStroke, axisTickFill } = useChartTheme();
  const { selectedWorkspace } = useWorkspace();
  const [data, setData] = useState<IngestionResponse | null>(null);
  const [connectors, setConnectors] = useState<DataConnectorsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [selectedConnector, setSelectedConnector] = useState<string>('all');

  useEffect(() => {
    if (!selectedWorkspace) return;
    const ws = selectedWorkspace;
    async function fetchData() {
      try {
        const [ing, conn] = await Promise.allSettled([getIngestion(ws), getDataConnectors(ws)]);
        if (ing.status === 'fulfilled') setData(ing.value);
        if (conn.status === 'fulfilled') setConnectors(conn.value);
      } finally {
        setLoading(false);
      }
    }
    setLoading(true);
    fetchData();
    const interval = setInterval(fetchData, 60000);
    return () => clearInterval(interval);
  }, [selectedWorkspace]);

  // Which table names to show based on selected connector
  const allowedTables = useMemo<Set<string> | null>(() => {
    if (selectedConnector === 'all' || !connectors) return null;
    const found = connectors.connectors.find((c) => c.connectorId === selectedConnector);
    return found ? new Set(found.associatedTables) : new Set();
  }, [selectedConnector, connectors]);

  if (loading) return <div className="loading">Loading ingestion data...</div>;
  if (!data) return <div className="error">Failed to load data</div>;

  const filteredTables = allowedTables
    ? data.tables.filter((t) => allowedTables.has(t.tableName))
    : data.tables;

  const top15 = filteredTables.slice(0, 15);
  const othersVolume = filteredTables.slice(15).reduce((sum, t) => sum + t.volumeGB, 0);

  const pieData = [
    ...top15.map((t) => ({ name: t.tableName, value: t.volumeGB })),
    ...(othersVolume > 0 ? [{ name: 'Others', value: othersVolume }] : []),
  ];

  const barData = top15.map((t) => ({
    name: t.tableName,
    volumeGB: t.volumeGB,
    costUSD: t.estimatedCostUSD,
  }));

  const filteredTotal = filteredTables.reduce((s, t) => s + t.volumeGB, 0);
  const filteredCost = filteredTables.reduce((s, t) => s + t.estimatedCostUSD, 0);

  return (
    <div className="page">
      <div className="page-header">
        <h2 className="page-title">Log Ingestion</h2>
        <span className="meta-text">
          {data.daysAnalyzed} days | Total: {data.totalVolumeGB.toFixed(1)} GB |
          Est. Cost: ${data.totalEstimatedCostUSD.toFixed(2)} |
          Last refresh: {new Date(data.lastRefreshed).toLocaleTimeString()}
        </span>
      </div>

      {/* Connector Filter */}
      {connectors && connectors.connectors.length > 0 && (
        <div className="filter-bar ingestion-filter">
          <button
            className={`filter-btn ${selectedConnector === 'all' ? 'active' : ''}`}
            onClick={() => setSelectedConnector('all')}
          >
            All Tables ({data.tables.length})
          </button>
          {connectors.connectors
            .filter((c) => c.associatedTables.length > 0)
            .map((c) => (
              <button
                key={c.connectorId}
                className={`filter-btn ${selectedConnector === c.connectorId ? 'active' : ''}`}
                onClick={() => setSelectedConnector(c.connectorId)}
              >
                {c.connectorName} ({c.associatedTables.length})
              </button>
            ))}
        </div>
      )}

      {/* Filtered summary bar */}
      {selectedConnector !== 'all' && (
        <div className="section ingestion-summary">
          <span className="meta-text">
            Showing <strong style={{ color: 'var(--text-primary)' }}>{filteredTables.length}</strong> tables &nbsp;|&nbsp;
            Volume: <strong style={{ color: 'var(--text-primary)' }}>{filteredTotal.toFixed(2)} GB</strong> &nbsp;|&nbsp;
            Est. Cost: <strong style={{ color: 'var(--text-primary)' }}>${filteredCost.toFixed(2)}</strong>
          </span>
        </div>
      )}

      {filteredTables.length === 0 ? (
        <div className="section">
          <p className="meta-text" style={{ textAlign: 'center', padding: '2rem 0' }}>
            No ingestion data found for this connector.
          </p>
        </div>
      ) : (
        <>
          <div className="charts-row">
            <div className="section chart-half">
              <h3>Volume by Table (GB)</h3>
              <div className="chart-container">
                <ResponsiveContainer width="100%" height={Math.max(300, top15.length * 28)}>
                  <BarChart data={barData} layout="vertical" margin={{ left: 150 }}>
                    <CartesianGrid strokeDasharray="3 3" stroke={gridStroke} />
                    <XAxis type="number" stroke={axisStroke} tick={{ fill: axisStroke, fontSize: 11 }} />
                    <YAxis
                      type="category"
                      dataKey="name"
                      stroke={axisStroke}
                      width={140}
                      tick={{ fill: axisTickFill, fontSize: 11 }}
                    />
                    <Tooltip
                      contentStyle={tooltipStyle}
                      formatter={(value: number | undefined) => value != null ? `${value.toFixed(2)} GB` : '0 GB'}
                    />
                    <Bar dataKey="volumeGB" fill="#3B82F6" radius={[0, 4, 4, 0]} style={{ filter: 'drop-shadow(0 4px 8px rgba(59, 130, 246, 0.3))' }} />
                  </BarChart>
                </ResponsiveContainer>
              </div>
            </div>

            <div className="section chart-half">
              <h3>Volume Distribution</h3>
              <div className="chart-container">
                <ResponsiveContainer width="100%" height={280}>
                  <PieChart>
                    <Pie
                      data={pieData}
                      dataKey="value"
                      nameKey="name"
                      cx="50%"
                      cy="50%"
                      outerRadius={110}
                      innerRadius={40}
                      paddingAngle={2}
                      label={renderLabel}
                      labelLine={false}
                    >
                      {pieData.map((_, index) => (
                        <Cell
                          key={`cell-${index}`}
                          fill={COLORS[index % COLORS.length]}
                          stroke="transparent"
                        />
                      ))}
                    </Pie>
                    <Tooltip
                      contentStyle={tooltipStyle}
                      formatter={(value: number | undefined) => value != null ? `${value.toFixed(2)} GB` : '0 GB'}
                    />
                  </PieChart>
                </ResponsiveContainer>
                <CustomLegend data={pieData} colors={COLORS} />
              </div>
            </div>
          </div>

          <div className="section">
            <h3>
              {selectedConnector === 'all'
                ? 'All Tables — Detailed Breakdown'
                : `${connectors?.connectors.find((c) => c.connectorId === selectedConnector)?.connectorName} — Tables`}
            </h3>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Table Name</th>
                  <th className="text-right">Volume (MB)</th>
                  <th className="text-right">Volume (GB)</th>
                  <th className="text-right">Daily Avg (MB)</th>
                  <th className="text-right">Est. Cost ($)</th>
                </tr>
              </thead>
              <tbody>
                {filteredTables.map((t) => (
                  <tr key={t.tableName}>
                    <td>{t.tableName}</td>
                    <td className="text-right">{t.volumeMB.toLocaleString()}</td>
                    <td className="text-right">{t.volumeGB.toFixed(2)}</td>
                    <td className="text-right">{t.dailyAverageMB.toLocaleString()}</td>
                    <td className="text-right">${t.estimatedCostUSD.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
              <tfoot>
                <tr>
                  <td><strong>Total</strong></td>
                  <td className="text-right">
                    <strong>{filteredTables.reduce((s, t) => s + t.volumeMB, 0).toLocaleString()}</strong>
                  </td>
                  <td className="text-right">
                    <strong>{filteredTotal.toFixed(2)}</strong>
                  </td>
                  <td className="text-right">-</td>
                  <td className="text-right">
                    <strong>${filteredCost.toFixed(2)}</strong>
                  </td>
                </tr>
              </tfoot>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
