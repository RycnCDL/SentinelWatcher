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
    public string Day { get; set; } = string.Empty; // "Mon 17", "Tue 18" etc.
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
    public string? Description { get; set; }
}

public class HealthFailureEvent
{
    public DateTime TimeGenerated { get; set; }
    public string ResourceType { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class SentinelHealthResponse
{
    public DateTime QueryTime { get; set; }
    public bool HealthMonitoringEnabled { get; set; }

    // Health Score (0 to 100)
    public int HealthScore { get; set; }

    // KPIs: last 24h
    public int TotalConnectors { get; set; }
    public int HealthyConnectors { get; set; }
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

    // Recent failures in last 24h
    public List<HealthFailureEvent> RecentFailures { get; set; } = new();
}
