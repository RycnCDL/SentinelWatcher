using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

public class SentinelHealthScoreTests
{
    [Fact]
    public void Compute_ReturnsSuccessShareAcrossAllResourceTypes()
    {
        var counts = new[]
        {
            new HealthRunCounts(Total: 100, Success: 90, Failure: 10), // data connectors
            new HealthRunCounts(Total: 50, Success: 50, Failure: 0),   // analytics rules
            new HealthRunCounts(Total: 50, Success: 40, Failure: 10),  // automation rules
        };

        // 200 events, 20 failures: 90 percent.
        Assert.Equal(90, SentinelHealthScore.Compute(counts));
    }

    [Fact]
    public void Compute_UsesFailureCountsNotSuccessCounts()
    {
        // The old calculation summed every second value of a flattened dictionary, which mixed
        // totals with success counts when each type had three values.
        var counts = new[]
        {
            new HealthRunCounts(Total: 10, Success: 0, Failure: 10),
            new HealthRunCounts(Total: 10, Success: 10, Failure: 0),
        };

        Assert.Equal(50, SentinelHealthScore.Compute(counts));
    }

    [Fact]
    public void Compute_ReturnsZeroWhenEverythingFailed()
    {
        Assert.Equal(0, SentinelHealthScore.Compute(new[] { new HealthRunCounts(7, 0, 7) }));
    }

    [Fact]
    public void Compute_Returns100WithoutEvents()
    {
        Assert.Equal(100, SentinelHealthScore.Compute(Array.Empty<HealthRunCounts>()));
    }

    [Theory]
    [InlineData("Data connector", SentinelHealthScore.DataConnector)]
    [InlineData("Analytics rule", SentinelHealthScore.AnalyticsRule)]
    [InlineData("Automation rule", SentinelHealthScore.AutomationRule)]
    [InlineData("Playbook", SentinelHealthScore.Playbook)]
    public void Summary_FindsResourceTypesAsReportedBySentinelHealth(string reported, string key)
    {
        var summary = SentinelHealthScore.CreateSummary();
        summary[SentinelHealthScore.NormalizeResourceType(reported)] = new HealthRunCounts(3, 2, 1);

        Assert.True(summary.TryGetValue(key, out var counts));
        Assert.Equal(3, counts.Total);
    }
}
