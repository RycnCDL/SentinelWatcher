using System.Net.Http.Json;
using Azure.Monitor.Query.Logs.Models;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;
using SentinelWatcher.Api.Tests.Fakes;

namespace SentinelWatcher.Api.Tests;

public class IncidentAttributionTests : IClassFixture<TestApp>
{
    private const string RulePrefix =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sentinel/providers" +
        "/Microsoft.OperationalInsights/workspaces/law-allowed/providers/Microsoft.SecurityInsights/alertRules/";

    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestApp _app;

    public IncidentAttributionTests(TestApp app)
    {
        _app = app;
    }

    private static List<(string Id, string Name, string Severity, bool Enabled)> Rules() => new()
    {
        (RulePrefix + "11111111-aaaa-bbbb-cccc-000000000001", "Brute force", "High", true),
        (RulePrefix + "11111111-aaaa-bbbb-cccc-000000000002", "Rare process", "Medium", true),
    };

    [Fact]
    public void IncidentsAreSplitIntoExistingMissingAndNoRule_AndTotalsAddUp()
    {
        // The case from the deployment: no incidents from existing rules, 48 from a deleted rule,
        // 50 Defender XDR and 16 Sentinel incidents without a rule. 114 incidents in total.
        var groups = new[]
        {
            new IncidentGroup("c7793e93-dea2-a530-2088-a0f559b143ee", "", 48, Now, "Suspicious_Scheduled_Task_Activity"),
            new IncidentGroup("", "Microsoft XDR", 50, Now, null),
            new IncidentGroup("", "Azure Sentinel", 16, Now.AddDays(-1), null),
        };

        var result = IncidentAttribution.Build(Rules(), groups, totalIncidents: 114, incidentsLoaded: true);

        Assert.Equal(114, result.TotalIncidents);
        Assert.Equal(0, result.IncidentsFromExistingRules);
        Assert.Equal(48, result.IncidentsFromMissingRules);
        Assert.Equal(66, result.IncidentsWithoutRule);
        Assert.Equal(result.TotalIncidents,
            result.IncidentsFromExistingRules + result.IncidentsFromMissingRules + result.IncidentsWithoutRule);

        var missing = Assert.Single(result.MissingRules);
        Assert.Equal("c7793e93-dea2-a530-2088-a0f559b143ee", missing.RuleId);
        Assert.Equal("Suspicious_Scheduled_Task_Activity", missing.SampleIncidentTitle);

        Assert.Contains(result.NoRuleSources, s => s.Label == "Defender XDR / no analytics rule" && s.IncidentCount == 50);
        Assert.Contains(result.NoRuleSources, s => s.Label == "Microsoft Sentinel / no analytics rule" && s.IncidentCount == 16);

        // No existing rule has incidents, so the "most incidents" list is empty (the UI shows an empty state).
        Assert.Empty(result.Top20MostIncidents);
        Assert.Equal(2, result.Top20LeastIncidents.Count);

        // No existing rule has incidents, so there is no last incident for them either.
        Assert.Null(result.LastIncidentFromExistingRules);
    }

    [Fact]
    public void LastIncidentFromExistingRules_IsTheNewestOverAllRules_NotTheTop20()
    {
        // 25 rules: 24 with 5 older incidents each, and one with a single, newest incident. The
        // single-incident rule is not in the "most incidents" top 20.
        var rules = Enumerable.Range(1, 25)
            .Select(i => (Id: $"{RulePrefix}22222222-aaaa-bbbb-cccc-{i:D12}", Name: $"Rule {i:D2}", Severity: "Medium", Enabled: true))
            .ToList();
        var groups = rules.Take(24)
            .Select((r, i) => new IncidentGroup(IncidentAttribution.RuleKey(r.Id), "", 5, Now.AddDays(-2).AddMinutes(i), "title"))
            .Append(new IncidentGroup(IncidentAttribution.RuleKey(rules[24].Id), "", 1, Now.AddHours(-1), "title"))
            // A deleted rule and a Defender XDR incident that are newer still do not count.
            .Append(new IncidentGroup("c7793e93-dea2-a530-2088-a0f559b143ee", "", 48, Now, "Suspicious_Scheduled_Task_Activity"))
            .Append(new IncidentGroup("", "Microsoft XDR", 3, Now, null))
            .ToList();

        var result = IncidentAttribution.Build(rules, groups, totalIncidents: 172, incidentsLoaded: true);

        Assert.DoesNotContain(result.Top20MostIncidents, r => r.RuleName == "Rule 25");
        Assert.Equal(Now.AddHours(-1), result.LastIncidentFromExistingRules);
        Assert.Equal(121, result.IncidentsFromExistingRules);
    }

    [Fact]
    public void IncidentsOfExistingRules_MatchByRuleGuidCaseInsensitive()
    {
        var groups = new[] { new IncidentGroup("11111111-aaaa-bbbb-cccc-000000000001", "", 3, Now, "title") };
        var rules = Rules();
        rules[0] = (rules[0].Id.ToUpperInvariant(), rules[0].Name, rules[0].Severity, rules[0].Enabled);

        var result = IncidentAttribution.Build(rules, groups, totalIncidents: 3, incidentsLoaded: true);

        var top = Assert.Single(result.Top20MostIncidents);
        Assert.Equal("Brute force", top.RuleName);
        Assert.Equal(3, top.IncidentCount);
        Assert.Empty(result.MissingRules);
    }

    [Fact]
    public async Task Endpoint_ReturnsTheBreakdownFromTheIncidentQuery()
    {
        var columns = new (string, LogsColumnType)[]
        {
            ("RuleId", LogsColumnType.String), ("Provider", LogsColumnType.String),
            ("IncidentCount", LogsColumnType.Long), ("LastIncident", LogsColumnType.Datetime),
            ("SampleTitle", LogsColumnType.String), ("TotalIncidents", LogsColumnType.Long),
        };
        var rows = new[]
        {
            new object?[] { "c7793e93-dea2-a530-2088-a0f559b143ee", "", 48L, Now.ToString("o"), "Suspicious_Scheduled_Task_Activity", 114L },
            new object?[] { "", "Microsoft XDR", 50L, Now.ToString("o"), "an XDR incident", 114L },
            new object?[] { "", "Azure Sentinel", 16L, Now.ToString("o"), "a Sentinel incident", 114L },
        };
        using var fake = new FakeAzureApp(_app,
            logs: q => q.Contains("SecurityIncident") ? LogsResults.Table(columns, rows) : LogsResults.Empty());
        var client = fake.CreateViewerClient();

        AnalyticsRulesResponse? result = null;
        for (var i = 0; i < 100 && result is not { IncidentsLoaded: true }; i++)
        {
            result = await client.GetFromJsonAsync<AnalyticsRulesResponse>($"/api/analyticsrules?{FakeAzureApp.WorkspaceQuery}");
            if (result is not { IncidentsLoaded: true }) await Task.Delay(50);
        }

        Assert.NotNull(result);
        Assert.True(result.IncidentsLoaded);
        Assert.Equal(114, result.TotalIncidents);
        Assert.Equal(48, result.IncidentsFromMissingRules);
        Assert.Equal(66, result.IncidentsWithoutRule);
        // Titles of incidents without a rule are not sent to the browser.
        Assert.DoesNotContain(typeof(NoRuleIncidents).GetProperties(), p => p.Name.Contains("Title"));
    }
}
