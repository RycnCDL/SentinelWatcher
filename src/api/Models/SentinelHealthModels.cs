namespace SentinelWatcher.Api.Models;

public class AutoDisabledRule
{
    public string Name { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateTime LastSeen { get; set; }
}

public class HealthTrendPoint
{
    public string Day { get; set; } = string.Empty; // ISO date, yyyy-MM-dd
    public int DataConnectorFailures { get; set; }
    public int AnalyticsRuleFailures { get; set; }
    public int AutomationFailures { get; set; }
    public int PlaybookFailures { get; set; }
}

public class ConnectorCurrentStatus
{
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? LastEvent { get; set; }

    /// <summary>No health event for this connector in the selected range.</summary>
    public bool IsStale { get; set; }

    /// <summary>Fixed failure category (see HealthFailureCategory), null for Success.</summary>
    public string? Category { get; set; }
}

public class HealthFailureEvent
{
    public DateTime TimeGenerated { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    /// <summary>Fixed failure category (see HealthFailureCategory). The raw description is only logged on the server.</summary>
    public string Category { get; set; } = string.Empty;
}

/// <summary>Failed health events of one SentinelResourceType in the last 24 hours.</summary>
public class HealthFailureCount
{
    public string ResourceType { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class SentinelHealthResponse
{
    public DateTime QueryTime { get; set; }
    public bool HealthMonitoringEnabled { get; set; }

    // Health Score (0 to 100)
    public int HealthScore { get; set; }

    // Connector health: connectors with a reported status, how many last reported Success
    // with a health event in the selected range, and how many had no event in that range
    public int TotalConnectors { get; set; }
    public int HealthyConnectors { get; set; }
    public int StaleConnectors { get; set; }

    // Run KPIs: last 24h
    public int TotalRuleRuns { get; set; }
    public int FailedRuleRuns { get; set; }
    public int AutoDisabledRulesCount { get; set; }
    public int TotalAutomationRuns { get; set; }
    public int FailedAutomationRuns { get; set; }

    // Auto-disabled rules: silent detection loss (critical!)
    public List<AutoDisabledRule> AutoDisabledRules { get; set; } = new();

    // 7-day failure trend per resource type
    public List<HealthTrendPoint> Trend { get; set; } = new();

    // Current connector status (latest per connector)
    public List<ConnectorCurrentStatus> ConnectorStatuses { get; set; } = new();

    // Failures in the last 24h per resource type, counted over all health events
    public List<HealthFailureCount> FailuresByType { get; set; } = new();
    public int TotalFailures { get; set; }

    // Newest failures in the last 24h, at most 50. Use the counts above for totals.
    public List<HealthFailureEvent> RecentFailures { get; set; } = new();
}
