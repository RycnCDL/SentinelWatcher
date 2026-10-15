using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

public class DataLakeTierTests
{
    private static readonly ConnectorThreshold Default = new(60, 120, EventDriven: false);

    private static readonly IReadOnlyDictionary<string, string> Tiers =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Syslog"] = TableTiers.Analytics,
            ["CommonSecurityLog"] = TableTiers.DataLake,
            ["AZFWNetworkRule"] = TableTiers.DataLake,
        };

    [Theory]
    [InlineData("Analytics", TableTiers.Analytics)]
    [InlineData("Basic", TableTiers.Basic)]
    [InlineData("Auxiliary", TableTiers.DataLake)]
    [InlineData("auxiliary", TableTiers.DataLake)]
    [InlineData(null, TableTiers.Unknown)]
    public void FromPlan_MapsLogAnalyticsPlans(string? plan, string expected)
    {
        Assert.Equal(expected, TableTiers.FromPlan(plan));
    }

    [Fact]
    public void Connector_WithOnlyDataLakeTables_IsUnknownNotError()
    {
        var health = new ConnectorHealth { AssociatedTables = { "CommonSecurityLog" } };

        SentinelService.ApplyHealthStatus(health, Tiers, Default, DateTime.UtcNow);

        Assert.Equal("Unknown", health.Status);
        Assert.StartsWith("Not available in data lake tier", health.ErrorMessage);
        Assert.Equal(new[] { "CommonSecurityLog" }, health.DataLakeTables);
    }

    [Fact]
    public void Connector_WithAnAnalyticsTableAndNoData_IsStillError()
    {
        var health = new ConnectorHealth { AssociatedTables = { "Syslog", "CommonSecurityLog" } };

        SentinelService.ApplyHealthStatus(health, Tiers, Default, DateTime.UtcNow);

        Assert.Equal("Error", health.Status);
    }

    [Fact]
    public void Connector_WithoutTierInformation_KeepsThePreviousBehavior()
    {
        var health = new ConnectorHealth { AssociatedTables = { "CommonSecurityLog" } };

        SentinelService.ApplyHealthStatus(health, new Dictionary<string, string>(), Default, DateTime.UtcNow);

        Assert.Equal("Error", health.Status);
        Assert.Empty(health.DataLakeTables);
    }

    [Fact]
    public void Dcr_WithOnlyDataLakeDestinations_IsUnknownNotError()
    {
        var dcr = new DcrStatus
        {
            Destinations = { new DcrDestination { TableName = "CommonSecurityLog", Tier = TableTiers.DataLake } },
        };

        DataCollectionRuleService.DetermineStatus(dcr);

        Assert.Equal("Unknown", dcr.Status);
        Assert.StartsWith("Not available in data lake tier", dcr.ErrorMessage);
    }

    [Fact]
    public void Dcr_IgnoresDataLakeDestinationsWhenOthersReceiveData()
    {
        var dcr = new DcrStatus
        {
            Destinations =
            {
                new DcrDestination { TableName = "Syslog", Tier = TableTiers.Analytics, IsReceivingData = true, LastLogReceived = DateTime.UtcNow },
                new DcrDestination { TableName = "CommonSecurityLog", Tier = TableTiers.DataLake },
            },
        };

        DataCollectionRuleService.DetermineStatus(dcr);

        Assert.Equal("Healthy", dcr.Status);
    }
}
