using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

/// <summary>
/// Incidents grouped by rule ID (lowercase GUID, last segment of the rule resource ID). Incidents
/// without a rule have an empty RuleId and are grouped by ProviderName instead.
/// </summary>
public sealed record IncidentGroup(string RuleId, string Provider, int IncidentCount, DateTime? LastIncident, string? SampleTitle);

/// <summary>
/// Assigns every incident to one of three sources so the totals add up: an existing analytics rule,
/// a rule that no longer exists (deleted or not found), or no analytics rule at all (for example
/// incidents created by Microsoft Defender XDR).
/// </summary>
public static class IncidentAttribution
{
    public static AnalyticsRulesResponse Build(
        IReadOnlyList<(string Id, string Name, string Severity, bool Enabled)> rules,
        IReadOnlyList<IncidentGroup> groups,
        int totalIncidents,
        bool incidentsLoaded)
    {
        var byRule = groups
            .Where(g => g.RuleId.Length > 0)
            .GroupBy(g => g.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var ruleKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stats = rules.Select(r =>
        {
            var key = RuleKey(r.Id);
            ruleKeys.Add(key);
            byRule.TryGetValue(key, out var group);
            return new AnalyticsRuleStats
            {
                RuleId = r.Id,
                RuleName = r.Name,
                Severity = r.Severity,
                Enabled = r.Enabled,
                IncidentCount = group?.IncidentCount ?? 0,
                LastIncident = group?.LastIncident,
            };
        }).ToList();

        var missing = byRule.Values
            .Where(g => !ruleKeys.Contains(g.RuleId))
            .OrderByDescending(g => g.IncidentCount)
            .Select(g => new MissingRuleIncidents
            {
                RuleId = g.RuleId,
                IncidentCount = g.IncidentCount,
                LastIncident = g.LastIncident,
                SampleIncidentTitle = g.SampleTitle,
            })
            .ToList();

        var withoutRule = groups
            .Where(g => g.RuleId.Length == 0)
            .GroupBy(g => g.Provider, StringComparer.OrdinalIgnoreCase)
            .Select(g => new NoRuleIncidents
            {
                Provider = g.Key,
                Label = NoRuleLabel(g.Key),
                IncidentCount = g.Sum(x => x.IncidentCount),
                LastIncident = g.Max(x => x.LastIncident),
            })
            .OrderByDescending(g => g.IncidentCount)
            .ToList();

        return new AnalyticsRulesResponse
        {
            LastRefreshed = DateTime.UtcNow,
            TotalRules = rules.Count,
            IncidentsLoaded = incidentsLoaded,
            TotalIncidents = totalIncidents,
            IncidentsFromExistingRules = stats.Sum(s => s.IncidentCount),
            IncidentsFromMissingRules = missing.Sum(m => m.IncidentCount),
            IncidentsWithoutRule = withoutRule.Sum(n => n.IncidentCount),
            MissingRules = missing,
            NoRuleSources = withoutRule,
            Top20MostIncidents = stats
                .Where(s => s.IncidentCount > 0)
                .OrderByDescending(s => s.IncidentCount)
                .ThenBy(s => s.RuleName)
                .Take(20)
                .ToList(),
            Top20LeastIncidents = stats
                .Where(s => s.Enabled)
                .OrderBy(s => s.IncidentCount)
                .ThenBy(s => s.RuleName)
                .Take(20)
                .ToList(),
        };
    }

    /// <summary>Rule key: last segment of a rule resource ID (or the ID itself), lowercase.</summary>
    public static string RuleKey(string ruleId) =>
        ruleId.TrimEnd('/').Split('/').Last().ToLowerInvariant();

    /// <summary>Display label for incidents without an analytics rule, by incident provider.</summary>
    public static string NoRuleLabel(string provider) => provider switch
    {
        "Microsoft XDR" or "Microsoft 365 Defender" => "Defender XDR / no analytics rule",
        "Azure Sentinel" or "Microsoft Sentinel" => "Microsoft Sentinel / no analytics rule",
        "" => "Unknown source / no analytics rule",
        _ => $"{provider} / no analytics rule",
    };
}
