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

/// <summary>Incidents that reference an analytics rule that no longer exists (deleted or not found).</summary>
public class MissingRuleIncidents
{
    /// <summary>Rule ID (GUID) as referenced by the incidents.</summary>
    public string RuleId { get; set; } = string.Empty;
    public int IncidentCount { get; set; }
    public DateTime? LastIncident { get; set; }

    /// <summary>Title of one of the incidents, as a hint to which rule this was.</summary>
    public string? SampleIncidentTitle { get; set; }
}

/// <summary>Incidents without an analytics rule, per incident provider (for example Microsoft XDR).</summary>
public class NoRuleIncidents
{
    public string Provider { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int IncidentCount { get; set; }
    public DateTime? LastIncident { get; set; }
}

public class AnalyticsRulesResponse
{
    /// <summary>Existing rules with at least one incident, most incidents first.</summary>
    public List<AnalyticsRuleStats> Top20MostIncidents { get; set; } = new();
    public List<AnalyticsRuleStats> Top20LeastIncidents { get; set; } = new();
    public int TotalRules { get; set; }

    /// <summary>False while only the rule list is loaded and incident counts are still being read.</summary>
    public bool IncidentsLoaded { get; set; }

    /// <summary>Distinct incidents created in the last 30 days.</summary>
    public int TotalIncidents { get; set; }

    // Where the incidents come from. An incident linked to several rules counts once per rule,
    // so the three numbers can add up to more than TotalIncidents.
    public int IncidentsFromExistingRules { get; set; }
    public int IncidentsFromMissingRules { get; set; }
    public int IncidentsWithoutRule { get; set; }
    public List<MissingRuleIncidents> MissingRules { get; set; } = new();
    public List<NoRuleIncidents> NoRuleSources { get; set; } = new();

    public DateTime LastRefreshed { get; set; }
}
