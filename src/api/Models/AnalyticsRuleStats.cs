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

/// <summary>Number of rules with one severity, over all rules.</summary>
public class RuleSeverityCount
{
    public string Severity { get; set; } = string.Empty;
    public int Total { get; set; }
    public int Enabled { get; set; }
}

public class AnalyticsRulesResponse
{
    // The two lists are for display and hold at most 20 rules each. Counts and breakdowns
    // use the fields below, which are computed from all rules.

    /// <summary>Existing rules with at least one incident, most incidents first (at most 20).</summary>
    public List<AnalyticsRuleStats> Top20MostIncidents { get; set; } = new();

    /// <summary>Enabled rules with the fewest incidents (at most 20).</summary>
    public List<AnalyticsRuleStats> Top20LeastIncidents { get; set; } = new();

    public int TotalRules { get; set; }
    public int EnabledRules { get; set; }
    public int DisabledRules { get; set; }

    /// <summary>Existing rules with at least one incident in the last 30 days. 0 until IncidentsLoaded.</summary>
    public int RulesWithIncidents { get; set; }

    /// <summary>Rules per severity: High, Medium, Low, Informational, then any other value.</summary>
    public List<RuleSeverityCount> SeverityCounts { get; set; } = new();

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

    /// <summary>Set when the incident counts could not be read. The rule list is still valid.</summary>
    public LoadError? IncidentsError { get; set; }

    /// <summary>Set when the latest refresh failed and this is older data.</summary>
    public LoadError? RefreshError { get; set; }
}
