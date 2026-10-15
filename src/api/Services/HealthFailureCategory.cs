namespace SentinelWatcher.Api.Services;

/// <summary>
/// Maps the free-text Description of a _SentinelHealth() event to a fixed category. The raw text
/// can contain account names, resource names or URLs, so only the category is sent to the browser;
/// the raw text is logged on the server.
/// </summary>
public static class HealthFailureCategory
{
    public const string AutoDisabled = "Rule auto-disabled";
    public const string Permissions = "Authentication or permissions";
    public const string Throttling = "Throttling or quota";
    public const string Timeout = "Timeout";
    public const string Connectivity = "Connection or service availability";
    public const string Query = "Query or rule logic";
    public const string Configuration = "Configuration or missing resource";
    public const string Automation = "Playbook or automation run";
    public const string Other = "Other failure";

    public static readonly IReadOnlyList<string> All = new[]
    {
        AutoDisabled, Permissions, Throttling, Timeout, Connectivity, Query, Configuration, Automation, Other,
    };

    // Checked in order; the first match wins.
    private static readonly (string Category, string[] Keywords)[] Rules =
    {
        (AutoDisabled, new[] { "auto-disabled", "autodisabled", "automatically disabled", "has been disabled", "was disabled" }),
        (Permissions, new[] { "unauthorized", "forbidden", "permission", "access denied", "accessdenied", "401", "403",
                              "credential", "token", "consent", "authoriz", "authentic" }),
        (Throttling, new[] { "throttl", "429", "quota", "too many requests", "rate limit" }),
        (Timeout, new[] { "timeout", "timed out", "time out" }),
        (Connectivity, new[] { "unavailable", "unreachable", "connection", "connect to", "network", "dns",
                               "500", "502", "503", "504", "service error", "internal server error" }),
        (Query, new[] { "query", "syntax", "semantic", "kql", "column", "table expression", "parser" }),
        (Configuration, new[] { "not found", "404", "does not exist", "doesn't exist", "deleted", "invalid",
                                "missing", "configur" }),
        (Automation, new[] { "playbook", "logic app", "automation", "trigger" }),
    };

    /// <summary>Category for a health event, or null when the event is a success.</summary>
    public static string? Categorize(string? status, string? description)
    {
        if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)) return null;

        var text = description ?? string.Empty;
        foreach (var (category, keywords) in Rules)
        {
            if (keywords.Any(k => Matches(text, k)))
                return category;
        }
        return Other;
    }

    // Status codes must stand alone, so "5000 records" does not match "500".
    private static bool Matches(string text, string keyword) =>
        keyword.All(char.IsDigit)
            ? System.Text.RegularExpressions.Regex.IsMatch(text, $@"(?<!\d){keyword}(?!\d)")
            : text.Contains(keyword, StringComparison.OrdinalIgnoreCase);
}
