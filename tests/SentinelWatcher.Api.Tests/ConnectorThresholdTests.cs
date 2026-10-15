using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

public class ConnectorThresholdTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Dictionary<string, string> NoTiers = new();

    private static ConnectorThresholds Thresholds(ConnectorHealthOptions? options = null) =>
        new(Options.Create(options ?? new ConnectorHealthOptions()));

    private static ConnectorHealth Connector(string type, double? gapMinutes, params string[] tables) => new()
    {
        ConnectorType = type,
        ConnectorName = type,
        AssociatedTables = tables.ToList(),
        LastLogReceived = gapMinutes is null ? null : Now.AddMinutes(-gapMinutes.Value),
    };

    [Fact]
    public void AzureActivity_SevenHoursWithoutLogs_IsHealthy()
    {
        // From the deployment: Azure Activity was Error after 7 hours without logs.
        var c = Connector("AzureActivity", 7 * 60, "AzureActivity");

        SentinelService.ApplyHealthStatus(c, NoTiers, Thresholds().For("AzureActivity"), Now);

        Assert.Equal("Healthy", c.Status);
    }

    [Fact]
    public void EventDrivenConnector_WithoutEventsFor24Hours_IsQuietNotError()
    {
        var c = Connector("AzureActivity", null, "AzureActivity");

        SentinelService.ApplyHealthStatus(c, NoTiers, Thresholds().For("AzureActivity"), Now);

        Assert.Equal("Quiet", c.Status);
        Assert.EndsWith(SentinelService.QuietMessageSuffix, c.ErrorMessage);
    }

    [Theory]
    [InlineData(30, "Healthy")]
    [InlineData(90, "Warning")]
    [InlineData(180, "Error")]
    public void ContinuousConnector_UsesTheDefaultThresholds(double gap, string status)
    {
        var c = Connector("Syslog", gap, "Syslog");

        SentinelService.ApplyHealthStatus(c, NoTiers, Thresholds().For("Syslog"), Now);

        Assert.Equal(status, c.Status);
    }

    [Fact]
    public void EntraId_HasALongerDefaultBecauseOfIngestionDelay()
    {
        var c = Connector("AzureActiveDirectory", 90, "SigninLogs");

        SentinelService.ApplyHealthStatus(c, NoTiers, Thresholds().For("AzureActiveDirectory"), Now);

        Assert.Equal("Healthy", c.Status);
    }

    [Fact]
    public void Configuration_OverridesDefaultsPerConnectorType()
    {
        var options = new ConnectorHealthOptions
        {
            WarningAfterMinutes = 30,
            ErrorAfterMinutes = 45,
            Connectors =
            {
                ["Syslog"] = new ConnectorThresholdOverride { WarningAfterMinutes = 240, ErrorAfterMinutes = 480 },
                ["AzureActivity"] = new ConnectorThresholdOverride { EventDriven = false },
            },
        };
        var thresholds = Thresholds(options);

        Assert.Equal(new ConnectorThreshold(240, 480, false), thresholds.For("Syslog"));
        Assert.Equal(new ConnectorThreshold(30, 45, false), thresholds.For("CommonEventFormat"));
        Assert.False(thresholds.For("AzureActivity").EventDriven);
        Assert.Equal(new ConnectorThreshold(240, 480, false), thresholds.For("syslog"));
    }

    [Fact]
    public void Overrides_BindFromConfigurationCaseInsensitively()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectorHealth:Connectors:syslog:WarningAfterMinutes"] = "240",
                ["ConnectorHealth:Connectors:SYSLOG:ErrorAfterMinutes"] = "480",
            })
            .Build();
        var options = new ConnectorHealthOptions();
        ConfigurationBinder.Bind(
            configuration.GetSection(ConnectorHealthOptions.SectionName), options);

        Assert.Equal(new ConnectorThreshold(240, 480, false), Thresholds(options).For("Syslog"));
    }

    [Fact]
    public void ErrorThreshold_IsNeverBelowTheWarningThreshold()
    {
        var options = new ConnectorHealthOptions { WarningAfterMinutes = 100, ErrorAfterMinutes = 10 };

        Assert.Equal(100, Thresholds(options).For("Syslog").ErrorAfterMinutes);
    }
}
