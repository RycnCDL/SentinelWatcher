// Why a section could not be loaded. The message is generic; details are in the server log.
export interface LoadError {
  code: string;
  message: string;
}

export interface AnalyticsRuleStats {
  ruleId: string;
  ruleName: string;
  severity: string;
  enabled: boolean;
  incidentCount: number;
  lastIncident: string | null;
}

// Incidents that reference an analytics rule that no longer exists.
export interface MissingRuleIncidents {
  ruleId: string;
  incidentCount: number;
  lastIncident: string | null;
  sampleIncidentTitle: string | null;
}

// Incidents without an analytics rule, per incident provider.
export interface NoRuleIncidents {
  provider: string;
  label: string;
  incidentCount: number;
  lastIncident: string | null;
}

// Number of rules with one severity, over all rules.
export interface RuleSeverityCount {
  severity: string;
  total: number;
  enabled: number;
}

export interface AnalyticsRulesResponse {
  // Display lists with at most 20 rules each. Never count them: use the totals below.
  // Existing rules with at least one incident, most incidents first.
  top20MostIncidents: AnalyticsRuleStats[];
  // Enabled rules with the fewest incidents.
  top20LeastIncidents: AnalyticsRuleStats[];
  // Counts over all rules.
  totalRules: number;
  enabledRules: number;
  disabledRules: number;
  // Existing rules with at least one incident (0 until incidentsLoaded).
  rulesWithIncidents: number;
  // High, Medium, Low, Informational, then any other value.
  severityCounts: RuleSeverityCount[];
  // False while only the rule list is loaded.
  incidentsLoaded: boolean;
  // Distinct incidents created in the last 30 days.
  totalIncidents: number;
  incidentsFromExistingRules: number;
  incidentsFromMissingRules: number;
  incidentsWithoutRule: number;
  missingRules: MissingRuleIncidents[];
  noRuleSources: NoRuleIncidents[];
  lastRefreshed: string;
  // The incident counts could not be read (the rule list is valid).
  incidentsError: LoadError | null;
  // The latest refresh failed; this is older data.
  refreshError: LoadError | null;
}

export interface TableIngestion {
  tableName: string;
  volumeMB: number;
  volumeGB: number;
  estimatedCostUSD: number;
  dailyAverageMB: number;
  // No Usage data for this table: volume is estimated from the record count (1 KB per record).
  isEstimated: boolean;
  tier: TableTier;
}

// Storage tier of a workspace table. Data lake tier tables can't be read through the
// Log Analytics query API, so their last log time is "not available", not "no data".
export type TableTier = 'Analytics' | 'Basic' | 'DataLake' | 'Unknown';

export interface IngestionResponse {
  tables: TableIngestion[];
  totalVolumeMB: number;
  totalVolumeGB: number;
  totalEstimatedCostUSD: number;
  daysAnalyzed: number;
  lastRefreshed: string;
  // Part of the data could not be read (the rest is valid).
  partialError: LoadError | null;
  refreshError: LoadError | null;
}

export interface ConnectorHealth {
  connectorId: string;
  connectorName: string;
  connectorType: string;
  // Quiet: an event-driven connector without recent events, which is not an error.
  status: 'Healthy' | 'Warning' | 'Error' | 'Quiet' | 'Unknown';
  lastLogReceived: string | null;
  gapMinutes: number | null;
  errorMessage: string | null;
  associatedTables: string[];
  dataLakeTables: string[];
}

export interface DataConnectorsResponse {
  connectors: ConnectorHealth[];
  healthyCount: number;
  warningCount: number;
  errorCount: number;
  quietCount: number;
  lastChecked: string;
  // The list may be incomplete.
  partialError: LoadError | null;
  refreshError: LoadError | null;
}

export interface DcrDestination {
  tableName: string;
  // Only checked when the table is in the selected workspace.
  destination: 'ThisWorkspace' | 'OtherWorkspace';
  lastLogReceived: string | null;
  isReceivingData: boolean;
  tier: TableTier;
}

export interface DcrStatus {
  dcrId: string;
  dcrName: string;
  status: 'Healthy' | 'Warning' | 'Error' | 'Unknown';
  location: string;
  dataSources: string[];
  destinations: DcrDestination[];
  lastDataReceived: string | null;
  errorMessage: string | null;
}

export interface DcrResponse {
  dataCollectionRules: DcrStatus[];
  totalCount: number;
  healthyCount: number;
  warningCount: number;
  errorCount: number;
  lastChecked: string;
  refreshError: LoadError | null;
}

// ── Sentinel Health ───────────────────────────────────────────────────────────

export interface AutoDisabledRule {
  name: string;
  resourceId: string;
  kind: string;
  lastSeen: string;
}

export interface HealthTrendPoint {
  day: string;
  dataConnectorFailures: number;
  analyticsRuleFailures: number;
  automationFailures: number;
  playbookFailures: number;
}

export interface ConnectorCurrentStatus {
  name: string;
  kind: string;
  status: string;
  lastEvent: string | null;
  // No health event for this connector in the selected range.
  isStale: boolean;
  // Fixed failure category; null for Success. Raw descriptions stay on the server.
  category: string | null;
}

export interface HealthFailureEvent {
  timeGenerated: string;
  resourceType: string;
  resourceName: string;
  kind: string;
  status: string;
  category: string;
}

export interface HealthFailureCount {
  resourceType: string;
  count: number;
}

export interface SentinelHealthResponse {
  queryTime: string;
  healthMonitoringEnabled: boolean;
  healthScore: number;

  // KPIs
  totalConnectors: number;
  healthyConnectors: number;
  staleConnectors: number;
  totalRuleRuns: number;
  failedRuleRuns: number;
  autoDisabledRulesCount: number;
  totalAutomationRuns: number;
  failedAutomationRuns: number;

  autoDisabledRules: AutoDisabledRule[];
  trend: HealthTrendPoint[];
  connectorStatuses: ConnectorCurrentStatus[];
  // Failures in the last 24h per resource type, counted over all health events.
  failuresByType: HealthFailureCount[];
  totalFailures: number;
  // Newest failures in the last 24h, at most 50. Never count them: use the fields above.
  recentFailures: HealthFailureEvent[];
}
