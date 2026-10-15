using System.Text.Json;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

public class DcrDestinationTests
{
    private const string OtherWorkspace =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-other" +
        "/providers/Microsoft.OperationalInsights/workspaces/law-central";

    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string> NoTiers = new(StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Microsoft-SecurityEvent", "SecurityEvent")]
    [InlineData("Microsoft-Table-SecurityEvent", "SecurityEvent")]
    [InlineData("Microsoft-Perf", "Perf")]
    [InlineData("Microsoft-CommonSecurityLog", "CommonSecurityLog")]
    [InlineData("Custom-FirewallLogs_CL", "FirewallLogs_CL")]
    [InlineData("SomethingElse", "SomethingElse")]
    public void MapStreamToTable_StripsStreamPrefixes(string stream, string table)
    {
        Assert.Equal(table, DataCollectionRuleService.MapStreamToTable(stream));
    }

    [Fact]
    public void OutputStream_IsTheDestinationTable_NotTheInputStream()
    {
        var props = Properties(TestApp.AllowedWorkspace, """
            [{ "streams": ["Custom-MyInput"], "destinations": ["la"], "outputStream": "Custom-MyTable_CL" }]
            """);
        var lastLog = new Dictionary<string, DateTime?> { ["MyTable_CL"] = Now.AddMinutes(-30) };

        var destinations = DataCollectionRuleService.BuildDestinations(props, TestApp.AllowedWorkspace, lastLog, NoTiers, Now);

        var d = Assert.Single(destinations);
        Assert.Equal("MyTable_CL", d.TableName);
        Assert.True(d.IsReceivingData);
        Assert.Equal(DataCollectionRuleService.ThisWorkspace, d.Destination);
    }

    [Fact]
    public void WorkspaceTransformationStream_MapsToTheTable()
    {
        var props = Properties(TestApp.AllowedWorkspace, """
            [{ "streams": ["Microsoft-Table-SecurityEvent"], "destinations": ["la"] }]
            """);
        var lastLog = new Dictionary<string, DateTime?> { ["SecurityEvent"] = Now.AddMinutes(-2) };

        var d = Assert.Single(DataCollectionRuleService.BuildDestinations(props, TestApp.AllowedWorkspace, lastLog, NoTiers, Now));

        Assert.Equal("SecurityEvent", d.TableName);
        Assert.True(d.IsReceivingData);
    }

    [Fact]
    public void OtherWorkspace_IsLabeledWithoutResourceIdAndNotChecked()
    {
        var props = Properties(OtherWorkspace, """
            [{ "streams": ["Microsoft-Syslog"], "destinations": ["la"] }]
            """);
        // Syslog has data in the selected workspace, which says nothing about the other one.
        var lastLog = new Dictionary<string, DateTime?> { ["Syslog"] = Now.AddMinutes(-1) };

        var dcr = new DcrStatus
        {
            Destinations = DataCollectionRuleService.BuildDestinations(props, TestApp.AllowedWorkspace, lastLog, NoTiers, Now),
        };
        DataCollectionRuleService.DetermineStatus(dcr);

        var d = Assert.Single(dcr.Destinations);
        Assert.Equal(DataCollectionRuleService.OtherWorkspace, d.Destination);
        Assert.False(d.IsReceivingData);
        Assert.Null(d.LastLogReceived);
        Assert.Equal("Unknown", dcr.Status);
        Assert.DoesNotContain(typeof(DcrDestination).GetProperties(), p => p.Name == "WorkspaceId");
    }

    [Fact]
    public void Status_IgnoresOtherWorkspaceDestinations()
    {
        var dcr = new DcrStatus
        {
            Destinations =
            {
                new DcrDestination { TableName = "Syslog", IsReceivingData = true, LastLogReceived = Now },
                new DcrDestination { TableName = "Syslog", Destination = DataCollectionRuleService.OtherWorkspace },
            },
        };

        DataCollectionRuleService.DetermineStatus(dcr);

        Assert.Equal("Healthy", dcr.Status);
    }

    [Fact]
    public void Destination_WithoutDataFor24Hours_IsNotReceiving()
    {
        var props = Properties(TestApp.AllowedWorkspace, """
            [{ "streams": ["Microsoft-Perf"], "destinations": ["la"] }]
            """);
        var lastLog = new Dictionary<string, DateTime?> { ["Perf"] = Now.AddHours(-25) };

        var d = Assert.Single(DataCollectionRuleService.BuildDestinations(props, TestApp.AllowedWorkspace, lastLog, NoTiers, Now));

        Assert.False(d.IsReceivingData);
    }

    private static JsonElement Properties(string workspaceId, string dataFlows) =>
        JsonDocument.Parse($$"""
            {
              "destinations": { "logAnalytics": [{ "name": "la", "workspaceResourceId": "{{workspaceId}}" }] },
              "dataFlows": {{dataFlows}}
            }
            """).RootElement.Clone();
}
