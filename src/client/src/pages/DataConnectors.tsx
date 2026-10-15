import { useEffect, useState, useCallback } from 'react';
import {
  CheckCircle,
  AlertTriangle,
  XCircle,
  HelpCircle,
  Moon,
} from 'lucide-react';
import { useWorkspace } from '../auth/useWorkspace';
import { getDataConnectors } from '../services/api';
import type { DataConnectorsResponse, ConnectorHealth, LoadError } from '../types';
import { LoadErrorState, PartialDataBanner, StaleDataBanner } from '../components/LoadErrorState';
import { loadErrorOf } from '../services/loadError';


const STATUS_CONFIG = {
  Healthy: {
    icon: CheckCircle,
    color: '#00D154',
    bg: 'rgba(0, 209, 84, 0.12)',
    glow: '0 0 8px rgba(0, 209, 84, 0.4)',
  },
  Warning: {
    icon: AlertTriangle,
    color: '#F59E0B',
    bg: 'rgba(245, 158, 11, 0.12)',
    glow: '0 0 8px rgba(245, 158, 11, 0.4)',
  },
  Error: {
    icon: XCircle,
    color: '#FF4D4D',
    bg: 'rgba(255, 77, 77, 0.12)',
    glow: '0 0 8px rgba(255, 77, 77, 0.4)',
  },
  Quiet: {
    icon: Moon,
    color: '#8B9BB4',
    bg: 'rgba(139, 155, 180, 0.12)',
    glow: '0 0 8px rgba(139, 155, 180, 0.3)',
  },
  Unknown: {
    icon: HelpCircle,
    color: '#6B7280',
    bg: 'rgba(107, 114, 128, 0.12)',
    glow: '0 0 8px rgba(107, 114, 128, 0.3)',
  },
};

function ConnectorCard({ connector }: { connector: ConnectorHealth }) {
  const config = STATUS_CONFIG[connector.status];
  const Icon = config.icon;
  const dataLake = new Set(connector.dataLakeTables);
  const onlyDataLake =
    connector.associatedTables.length > 0 && connector.dataLakeTables.length === connector.associatedTables.length;

  return (
    <div className="connector-card" style={{ borderColor: config.color }}>
      <div className="connector-card__header">
        <Icon size={20} color={config.color} />
        <span className="connector-card__name">{connector.connectorName}</span>
        <span
          className="connector-card__status"
          style={{ backgroundColor: config.bg, color: config.color, boxShadow: config.glow }}
        >
          {connector.status}
        </span>
      </div>
      <div className="connector-card__details">
        <div className="detail-row">
          <span className="detail-label">Type:</span>
          <span>{connector.connectorType}</span>
        </div>
        <div className="detail-row">
          <span className="detail-label">Tables:</span>
          <span>
            {connector.associatedTables.length > 0
              ? connector.associatedTables
                  .map((t) => (dataLake.has(t) ? `${t} (data lake tier)` : t))
                  .join(', ')
              : 'N/A'}
          </span>
        </div>
        <div className="detail-row">
          <span className="detail-label">Last Log:</span>
          <span>
            {connector.lastLogReceived
              ? new Date(connector.lastLogReceived).toLocaleString()
              : onlyDataLake
                ? 'Not available in data lake tier'
                : 'No data'}
          </span>
        </div>
        {connector.gapMinutes != null && (
          <div className="detail-row">
            <span className="detail-label">Gap:</span>
            <span>
              {connector.gapMinutes < 60
                ? `${connector.gapMinutes.toFixed(0)} min`
                : `${(connector.gapMinutes / 60).toFixed(1)} hrs`}
            </span>
          </div>
        )}
        {connector.errorMessage && (
          <div className="detail-row error-text">{connector.errorMessage}</div>
        )}
      </div>
    </div>
  );
}

export default function DataConnectors() {
  const { selectedWorkspace } = useWorkspace();
  const [data, setData] = useState<DataConnectorsResponse | null>(null);
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const [filter, setFilter] = useState<string>('all');
  const [error, setError] = useState<LoadError | null>(null);
  const loading = !selectedWorkspace || settledKey !== selectedWorkspace.resourceId;

  // State is only set in promise callbacks, so this can run directly from the effect.
  const fetchData = useCallback((): Promise<void> => {
    if (!selectedWorkspace) return Promise.resolve();
    const ws = selectedWorkspace;
    return getDataConnectors(ws)
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

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 60000);
    return () => clearInterval(interval);
  }, [fetchData]);

  if (loading) return <div className="loading">Loading data connectors...</div>;
  if (!data) {
    return (
      <div className="page">
        <div className="page-header">
          <h2 className="page-title">Data Connectors: Data freshness</h2>
        </div>
        {error && <LoadErrorState error={error} what="Data connectors" />}
      </div>
    );
  }

  const filtered =
    filter === 'all'
      ? data.connectors
      : data.connectors.filter((c) => c.status === filter);

  return (
    <div className="page">
      <div className="page-header">
        <h2 className="page-title">Data Connectors: Data freshness</h2>
        <span className="meta-text" title="Status comes from the newest record in each connector's tables. Connector health reported by Microsoft Sentinel is on the Sentinel Health page.">
          Based on the newest record per table | Last checked: {new Date(data.lastChecked).toLocaleTimeString()}
        </span>
      </div>

      <StaleDataBanner error={data.refreshError} since={data.lastChecked} />
      <PartialDataBanner error={data.partialError} />

      <div className="filter-bar">
        {['all', 'Healthy', 'Warning', 'Error', 'Quiet', 'Unknown'].map((f) => (
          <button
            key={f}
            className={`filter-btn ${filter === f ? 'active' : ''}`}
            onClick={() => setFilter(f)}
          >
            {f === 'all' ? `All (${data.connectors.length})` : `${f} (${data.connectors.filter((c) => c.status === f).length})`}
          </button>
        ))}
      </div>

      <div className="connectors-grid">
        {filtered.map((c) => (
          <ConnectorCard key={c.connectorId} connector={c} />
        ))}
        {filtered.length === 0 && (
          <div className="empty-state">No connectors match this filter</div>
        )}
      </div>
    </div>
  );
}
