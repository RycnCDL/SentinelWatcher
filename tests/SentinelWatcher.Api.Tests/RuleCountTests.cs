using System.Net.Http.Json;
using System.Text.Json;
using Azure.Monitor.Query.Logs.Models;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;
using SentinelWatcher.Api.Tests.Fakes;

namespace SentinelWatcher.Api.Tests;

/// <summary>
/// Rule counts and the severity breakdown cover all rules. The top 20 lists are for display only;
/// counting them gave "20 enabled" for a workspace with 35 enabled rules.
/// </summary>
public class RuleCountTests : IClassFixture<TestApp>
{
    private const string RulePrefix =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sentinel/providers" +
        "/Microsoft.OperationalInsights/workspaces/law-allowed/providers/Microsoft.SecurityInsights/alertRules/";

    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestApp _app;

    public RuleCountTests(TestApp app)
    {
        _app = app;
    }

    /// <summary>
    /// The workspace from the local test: 34 enabled Scheduled rules (High 4, Medium 19, Low 7,
    /// Informational 4) and the enabled Fusion rule (High).
    /// </summary>
    private static List<(string Id, string Name, string Severity, bool Enabled, string Kind)> WorkspaceRules()
    {
        var rules = new List<(string Id, string Name, string Severity, bool Enabled, string Kind)>();
        void Add(string severity, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var n = rules.Count + 1;
                rules.Add(($"{RulePrefix}00000000-0000-0000-0000-{n:D12}", $"Rule {n:D2} {severity}", severity, true, "Scheduled"));
            }
        }
        Add("High", 4);
        Add("Medium", 19);
        Add("Low", 7);
        Add("Informational", 4);
        rules.Add(($"{RulePrefix}BuiltInFusion", "Advanced Multistage Attack Detection", "High", true, "Fusion"));
        return rules;
    }

    private static string AlertRulesJson(IEnumerable<(string Id, string Name, string Severity, bool Enabled, string Kind)> rules) =>
        JsonSerializer.Serialize(new
        {
            value = rules.Select(r => new
            {
                id = r.Id,
                name = r.Id.Split('/').Last(),
                kind = r.Kind,
                properties = new { displayName = r.Name, severity = r.Severity, enabled = r.Enabled },
            }),
        });

    [Fact]
    public async Task Endpoint_CountsAllRules_NotTheTop20Lists()
    {
        var rules = WorkspaceRules();

        // 25 rules with incidents, more than fit in the "most incidents" list.
        var columns = new (string, LogsColumnType)[]
        {
            ("RuleId", LogsColumnType.String), ("Provider", LogsColumnType.String),
            ("IncidentCount", LogsColumnType.Long), ("LastIncident", LogsColumnType.Datetime),
            ("SampleTitle", LogsColumnType.String), ("TotalIncidents", LogsColumnType.Long),
        };
        var rows = rules.Take(25)
            .Select(r => new object?[] { IncidentAttribution.RuleKey(r.Id), "", 2L, Now.ToString("o"), "title", 50L })
            .ToArray();

        using var fake = new FakeAzureApp(_app,
            logs: q => q.Contains("SecurityIncident") ? LogsResults.Table(columns, rows) : LogsResults.Empty(),
            arm: path => path.EndsWith("/alertRules", StringComparison.OrdinalIgnoreCase)
                ? AlertRulesJson(rules)
                : FakeAzureApp.EmptyCollections(path));
        var client = fake.CreateViewerClient();

        AnalyticsRulesResponse? result = null;
        for (var i = 0; i < 100 && result is not { IncidentsLoaded: true }; i++)
        {
            var response = await client.GetAsync($"/api/analyticsrules?{FakeAzureApp.WorkspaceQuery}");
            if (response.IsSuccessStatusCode)
                result = await response.Content.ReadFromJsonAsync<AnalyticsRulesResponse>();
            if (result is not { IncidentsLoaded: true }) await Task.Delay(50);
        }

        Assert.NotNull(result);
        Assert.True(result.IncidentsLoaded);

        // The display lists are capped at 20.
        Assert.Equal(20, result.Top20MostIncidents.Count);
        Assert.Equal(20, result.Top20LeastIncidents.Count);

        // The counts are not.
        Assert.Equal(35, result.TotalRules);
        Assert.Equal(35, result.EnabledRules);
        Assert.Equal(0, result.DisabledRules);
        Assert.Equal(25, result.RulesWithIncidents);

        Assert.Equal(
            new[] { ("High", 5, 5), ("Medium", 19, 19), ("Low", 7, 7), ("Informational", 4, 4) },
            result.SeverityCounts.Select(c => (c.Severity, c.Total, c.Enabled)).ToArray());
        Assert.Equal(result.TotalRules, result.SeverityCounts.Sum(c => c.Total));
    }

    [Fact]
    public void DisabledRules_AreCountedPerSeverity()
    {
        var rules = WorkspaceRules()
            .Select((r, i) => (r.Id, r.Name, r.Severity, Enabled: i % 10 != 0))
            .ToList();
        rules.Add(($"{RulePrefix}custom", "Rule with a custom severity", "Critical", false));

        var result = IncidentAttribution.Build(rules, Array.Empty<IncidentGroup>(), totalIncidents: 0, incidentsLoaded: true);

        // Rules 1, 11, 21 and 31 are disabled (High, Medium, Medium, Informational), plus the custom one.
        Assert.Equal(36, result.TotalRules);
        Assert.Equal(31, result.EnabledRules);
        Assert.Equal(5, result.DisabledRules);
        Assert.Equal(0, result.RulesWithIncidents);
        Assert.Equal(
            new[] { ("High", 5, 4), ("Medium", 19, 17), ("Low", 7, 7), ("Informational", 4, 3), ("Critical", 1, 0) },
            result.SeverityCounts.Select(c => (c.Severity, c.Total, c.Enabled)).ToArray());
    }
}
