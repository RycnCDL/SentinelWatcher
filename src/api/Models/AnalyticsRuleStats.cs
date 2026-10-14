namespace SentinelWatcher.Api.Models;

public class AnalyticsRuleStats
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int IncidentCount { get; set; }
    public DateTime? LastIncident { get; set; }
}

public class AnalyticsRulesResponse
{
    public List<AnalyticsRuleStats> Top20MostIncidents { get; set; } = new();
    public List<AnalyticsRuleStats> Top20LeastIncidents { get; set; } = new();
    public int TotalRules { get; set; }
    public int TotalIncidents { get; set; }
    public DateTime LastRefreshed { get; set; }
}
