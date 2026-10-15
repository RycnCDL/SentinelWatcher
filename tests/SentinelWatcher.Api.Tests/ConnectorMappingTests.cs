using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

public class ConnectorMappingTests
{
    [Theory]
    [InlineData("ThreatIntelligenceIndicator")]
    [InlineData("ThreatIntelIndicators")]
    [InlineData("ThreatIntelObjects")]
    public void ThreatIntelligenceTables_MapToTheThreatIntelligenceConnector(string table)
    {
        Assert.True(SentinelService.TryMapTableToConnector(table, out var connector));
        Assert.Equal("ThreatIntelligence", connector.ConnectorType);
    }

    [Theory]
    [InlineData("ThreatIntelligence")]
    [InlineData("ThreatIntelligenceTaxii")]
    [InlineData("MicrosoftThreatIntelligence")]
    public void ThreatIntelligenceConnectorKinds_IncludeTheNewTables(string kind)
    {
        var tables = SentinelService.MapConnectorKindToTables(kind);

        Assert.Contains("ThreatIntelligenceIndicator", tables);
        Assert.Contains("ThreatIntelIndicators", tables);
        Assert.Contains("ThreatIntelObjects", tables);
    }
}
