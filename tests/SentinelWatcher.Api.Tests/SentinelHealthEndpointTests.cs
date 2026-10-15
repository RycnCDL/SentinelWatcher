using System.Net.Http.Json;
using Azure.Monitor.Query.Logs.Models;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;
using SentinelWatcher.Api.Tests.Fakes;

namespace SentinelWatcher.Api.Tests;

public class SentinelHealthEndpointTests : IClassFixture<TestApp>
{
    private const string SecretText = "user admin@contoso.example from 203.0.113.7 on host DC01";

    private readonly TestApp _app;

    public SentinelHealthEndpointTests(TestApp app)
    {
        _app = app;
    }

    [Theory]
    [InlineData("Failure", "The connector failed: 403 Forbidden for the service principal", HealthFailureCategory.Permissions)]
    [InlineData("Failure", "Request was throttled (429)", HealthFailureCategory.Throttling)]
    [InlineData("Failure", "The query timed out", HealthFailureCategory.Timeout)]
    [InlineData("Failure", "Rule was auto-disabled after consecutive failures", HealthFailureCategory.AutoDisabled)]
    [InlineData("Failure", "Something unexpected", HealthFailureCategory.Other)]
    [InlineData("Failure", "Dropped 5000 records", HealthFailureCategory.Other)]
    [InlineData("Failure", "HTTP 503 from the source", HealthFailureCategory.Connectivity)]
    [InlineData("Failure", null, HealthFailureCategory.Other)]
    [InlineData("Success", "Data fetch succeeded", null)]
    public void Categorize_MapsDescriptionsToFixedCategories(string status, string? description, string? expected)
    {
        Assert.Equal(expected, HealthFailureCategory.Categorize(status, description));
    }

    [Fact]
    public async Task Endpoint_SendsCategoriesNotDescriptions_AndMarksStaleConnectors()
    {
        var now = DateTimeOffset.UtcNow;
        using var fake = new FakeAzureApp(_app, logs: query =>
        {
            if (query.Contains("take 1"))
                return LogsResults.Table(new[] { ("TimeGenerated", LogsColumnType.Datetime) }, new[] { new object?[] { now.ToString("o") } });
            if (query.Contains("arg_max(TimeGenerated, Status, Description)"))
                return LogsResults.Table(
                    new[]
                    {
                        ("SentinelResourceName", LogsColumnType.String), ("SentinelResourceKind", LogsColumnType.String),
                        ("TimeGenerated", LogsColumnType.Datetime), ("Status", LogsColumnType.String),
                        ("Description", LogsColumnType.String),
                    },
                    new[]
                    {
                        new object?[] { "Microsoft Defender XDR", "MicrosoftThreatProtection", now.AddHours(-2).ToString("o"), "Success", "ok" },
                        new object?[] { "AWS", "AmazonWebServicesS3", now.AddHours(-1).ToString("o"), "Failure", $"Unauthorized: {SecretText}" },
                        new object?[] { "MTPAlerts", "MicrosoftThreatProtection", now.AddDays(-57).ToString("o"), "Success", "ok" },
                    });
            if (query.Contains("take 50"))
                return LogsResults.Table(
                    new[]
                    {
                        ("TimeGenerated", LogsColumnType.Datetime), ("SentinelResourceType", LogsColumnType.String),
                        ("SentinelResourceName", LogsColumnType.String), ("SentinelResourceKind", LogsColumnType.String),
                        ("Status", LogsColumnType.String), ("Description", LogsColumnType.String),
                    },
                    new[] { new object?[] { now.ToString("o"), "Playbook", "Notify-SOC", "LogicApp", "Failure", $"Playbook failed for {SecretText}" } });
            return LogsResults.Empty();
        });
        var client = fake.CreateViewerClient();

        var raw = await client.GetStringAsync($"/api/sentinelhealth?{FakeAzureApp.WorkspaceQuery}&days=7");
        var result = await client.GetFromJsonAsync<SentinelHealthResponse>($"/api/sentinelhealth?{FakeAzureApp.WorkspaceQuery}&days=7");

        Assert.DoesNotContain("contoso", raw);
        Assert.DoesNotContain("203.0.113.7", raw);
        Assert.DoesNotContain("description", raw, StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(result);
        Assert.Equal(3, result.TotalConnectors);
        Assert.Equal(1, result.HealthyConnectors); // Defender XDR; AWS failed; MTPAlerts is stale
        Assert.Equal(1, result.StaleConnectors);
        Assert.True(result.ConnectorStatuses.Single(c => c.Name == "MTPAlerts").IsStale);
        Assert.Equal(HealthFailureCategory.Permissions, result.ConnectorStatuses.Single(c => c.Name == "AWS").Category);
        Assert.Null(result.ConnectorStatuses.Single(c => c.Name == "Microsoft Defender XDR").Category);
        Assert.Equal(HealthFailureCategory.Automation, Assert.Single(result.RecentFailures).Category);
    }

    [Fact]
    public async Task FailureCounts_CoverAllFailures_NotTheNewest50()
    {
        var now = DateTimeOffset.UtcNow;
        using var fake = new FakeAzureApp(_app, logs: query =>
        {
            if (query.Contains("take 1"))
                return LogsResults.Table(new[] { ("TimeGenerated", LogsColumnType.Datetime) }, new[] { new object?[] { now.ToString("o") } });
            if (query.Contains("Failure = countif"))
                return LogsResults.Table(
                    new[]
                    {
                        ("SentinelResourceType", LogsColumnType.String), ("Total", LogsColumnType.Long),
                        ("Success", LogsColumnType.Long), ("Failure", LogsColumnType.Long),
                    },
                    new[]
                    {
                        new object?[] { "Analytics Rule", 500L, 430L, 70L },
                        new object?[] { "Playbook", 10L, 7L, 3L },
                        new object?[] { "Data connector", 24L, 24L, 0L },
                    });
            if (query.Contains("take 50"))
                return LogsResults.Table(
                    new[]
                    {
                        ("TimeGenerated", LogsColumnType.Datetime), ("SentinelResourceType", LogsColumnType.String),
                        ("SentinelResourceName", LogsColumnType.String), ("SentinelResourceKind", LogsColumnType.String),
                        ("Status", LogsColumnType.String), ("Description", LogsColumnType.String),
                    },
                    Enumerable.Range(0, 50)
                        .Select(i => new object?[] { now.AddMinutes(-i).ToString("o"), "Analytics Rule", $"Rule {i}", "Scheduled", "Failure", "failed" })
                        .ToArray());
            return LogsResults.Empty();
        });
        var client = fake.CreateViewerClient();

        var result = await client.GetFromJsonAsync<SentinelHealthResponse>($"/api/sentinelhealth?{FakeAzureApp.WorkspaceQuery}&days=7");

        Assert.NotNull(result);
        Assert.Equal(50, result.RecentFailures.Count);
        Assert.Equal(73, result.TotalFailures);
        Assert.Equal(
            new[] { ("Analytics Rule", 70), ("Playbook", 3) },
            result.FailuresByType.Select(f => (f.ResourceType, f.Count)).ToArray());
    }
}
