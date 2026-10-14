import { useEffect, useState, useCallback } from 'react';
import {
  CheckCircle,
  AlertTriangle,
  XCircle,
  HelpCircle,
} from 'lucide-react';
import { useWorkspace } from '../auth/useWorkspace';
import { getDataConnectors } from '../services/api';
import type { DataConnectorsResponse, ConnectorHealth } from '../types';

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
              ? connector.associatedTables.join(', ')
              : 'N/A'}
          </span>
        </div>
        <div className="detail-row">
          <span className="detail-label">Last Log:</span>
          <span>
            {connector.lastLogReceived
              ? new Date(connector.lastLogReceived).toLocaleString()
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
  const loading = !selectedWorkspace || settledKey !== selectedWorkspace.resourceId;

  const fetchData = useCallback(async () => {
    if (!selectedWorkspace) return;
    try {
      const result = await getDataConnectors(selectedWorkspace);
      setData(result);
    } finally {
      setSettledKey(selectedWorkspace.resourceId);
    }
  }, [selectedWorkspace]);

  useEffect(() => {
    fetchData();
    const interval = setInterval(fetchData, 60000);
    return () => clearInterval(interval);
  }, [fetchData]);

  if (loading) return <div className="loading">Loading data connectors...</div>;
  if (!data) return <div className="error">Failed to load data</div>;

  const filtered =
    filter === 'all'
      ? data.connectors
      : data.connectors.filter((c) => c.status === filter);

  return (
    <div className="page">
      <div className="page-header">
        <h2 className="page-title">Data Connectors</h2>
        <span className="meta-text">
          Last checked: {new Date(data.lastChecked).toLocaleTimeString()}
        </span>
      </div>

      <div className="filter-bar">
        {['all', 'Healthy', 'Warning', 'Error', 'Unknown'].map((f) => (
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
