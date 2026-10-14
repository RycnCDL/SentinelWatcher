import { useEffect, useState } from 'react';
import {
  CheckCircle,
  AlertTriangle,
  XCircle,
  HelpCircle,
} from 'lucide-react';
import { useWorkspace } from '../auth/useWorkspace';
import { getDcr } from '../services/api';
import type { DcrResponse } from '../types';
import StatusCard from '../components/StatusCard';

const STATUS_ICON = {
  Healthy: CheckCircle,
  Warning: AlertTriangle,
  Error: XCircle,
  Unknown: HelpCircle,
};

const STATUS_COLOR: Record<string, string> = {
  Healthy: '#00D154',
  Warning: '#F59E0B',
  Error: '#FF4D4D',
  Unknown: '#6B7280',
};

export default function DataCollectionRules() {
  const { selectedWorkspace } = useWorkspace();
  const [data, setData] = useState<DcrResponse | null>(null);
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const [expanded, setExpanded] = useState<string | null>(null);
  const loading = !selectedWorkspace || settledKey !== selectedWorkspace.resourceId;

  useEffect(() => {
    if (!selectedWorkspace) return;
    const ws = selectedWorkspace;
    async function fetchData() {
      try {
        const result = await getDcr(ws);
        setData(result);
      } finally {
        setSettledKey(ws.resourceId);
      }
    }
    fetchData();
    const interval = setInterval(fetchData, 60000);
    return () => clearInterval(interval);
  }, [selectedWorkspace]);

  if (loading) return <div className="loading">Loading DCR data...</div>;
  if (!data) return <div className="error">Failed to load data</div>;

  return (
    <div className="page">
      <div className="page-header">
        <h2 className="page-title">Data Collection Rules</h2>
        <span className="meta-text">
          Last checked: {new Date(data.lastChecked).toLocaleTimeString()}
        </span>
      </div>

      <div className="cards-grid">
        <StatusCard
          title="Total DCRs"
          value={data.totalCount}
          icon={<CheckCircle size={24} />}
          color="blue"
        />
        <StatusCard
          title="Healthy"
          value={data.healthyCount}
          icon={<CheckCircle size={24} />}
          color="green"
        />
        <StatusCard
          title="Warning"
          value={data.warningCount}
          icon={<AlertTriangle size={24} />}
          color="yellow"
        />
        <StatusCard
          title="Error"
          value={data.errorCount}
          icon={<XCircle size={24} />}
          color="red"
        />
      </div>

      <div className="section">
        <table className="data-table">
          <thead>
            <tr>
              <th>Status</th>
              <th>DCR Name</th>
              <th>Location</th>
              <th>Data Sources</th>
              <th>Destinations</th>
              <th>Last Data</th>
              <th>Details</th>
            </tr>
          </thead>
          <tbody>
            {data.dataCollectionRules.map((dcr) => {
              const Icon = STATUS_ICON[dcr.status];
              const isExpanded = expanded === dcr.dcrId;
              return (
                <>
                  <tr key={dcr.dcrId}>
                    <td>
                      <Icon size={18} color={STATUS_COLOR[dcr.status]} />
                    </td>
                    <td>{dcr.dcrName}</td>
                    <td>{dcr.location}</td>
                    <td>{dcr.dataSources.length}</td>
                    <td>{dcr.destinations.length}</td>
                    <td>
                      {dcr.lastDataReceived
                        ? new Date(dcr.lastDataReceived).toLocaleString()
                        : 'No data'}
                    </td>
                    <td>
                      <button
                        className="btn btn--small"
                        onClick={() =>
                          setExpanded(isExpanded ? null : dcr.dcrId)
                        }
                      >
                        {isExpanded ? 'Hide' : 'Show'}
                      </button>
                    </td>
                  </tr>
                  {isExpanded && (
                    <tr key={`${dcr.dcrId}-details`} className="expanded-row">
                      <td colSpan={7}>
                        <div className="dcr-details">
                          {dcr.errorMessage && (
                            <div className="alert alert--error">
                              <AlertTriangle size={14} />
                              {dcr.errorMessage}
                            </div>
                          )}
                          <div className="dcr-details__grid">
                            <div>
                              <h4>Data Sources</h4>
                              <ul>
                                {dcr.dataSources.map((ds, i) => (
                                  <li key={i}>{ds}</li>
                                ))}
                                {dcr.dataSources.length === 0 && (
                                  <li className="muted">None detected</li>
                                )}
                              </ul>
                            </div>
                            <div>
                              <h4>Destinations</h4>
                              {dcr.destinations.map((dest, i) => (
                                <div key={i} className="dest-item">
                                  <span className="dest-table">
                                    {dest.tableName}
                                  </span>
                                  <span
                                    className={`dest-status ${dest.isReceivingData ? 'receiving' : 'not-receiving'}`}
                                  >
                                    {dest.isReceivingData
                                      ? 'Receiving'
                                      : 'Not receiving'}
                                  </span>
                                  {dest.lastLogReceived && (
                                    <span className="dest-last">
                                      Last:{' '}
                                      {new Date(
                                        dest.lastLogReceived
                                      ).toLocaleString()}
                                    </span>
                                  )}
                                </div>
                              ))}
                              {dcr.destinations.length === 0 && (
                                <span className="muted">None detected</span>
                              )}
                            </div>
                          </div>
                        </div>
                      </td>
                    </tr>
                  )}
                </>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
