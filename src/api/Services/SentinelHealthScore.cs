namespace SentinelWatcher.Api.Services;

/// <summary>Run counts for one SentinelResourceType from _SentinelHealth() over the last 24 hours.</summary>
public readonly record struct HealthRunCounts(int Total, int Success, int Failure);

public static class SentinelHealthScore
{
    /// <summary>Resource type keys as produced by <see cref="NormalizeResourceType"/>.</summary>
    public const string DataConnector = "DataConnector";
    public const string AnalyticsRule = "AnalyticsRule";
    public const string AutomationRule = "AutomationRule";
    public const string Playbook = "Playbook";

    /// <summary>
    /// SentinelResourceType values are "Data connector", "Analytics rule" and so on. Spaces are
    /// removed; lookups must use a case-insensitive dictionary (see <see cref="CreateSummary"/>).
    /// </summary>
    public static string NormalizeResourceType(string? resourceType) =>
        (resourceType ?? string.Empty).Replace(" ", string.Empty);

    public static Dictionary<string, HealthRunCounts> CreateSummary() =>
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Percentage of health events that succeeded across all resource types (0 to 100).
    /// Returns 100 when there are no events.
    /// </summary>
    public static int Compute(IEnumerable<HealthRunCounts> counts)
    {
        long total = 0, failed = 0;
        foreach (var c in counts)
        {
            total += c.Total;
            failed += c.Failure;
        }

        if (total <= 0) return 100;
        var score = (int)Math.Round(100.0 * (total - failed) / total);
        return Math.Clamp(score, 0, 100);
    }
}
