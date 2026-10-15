using System.Net;
using SentinelWatcher.Api.Tests.Fakes;

namespace SentinelWatcher.Api.Tests;

/// <summary>
/// All endpoints read through WorkspaceCacheManager, so a first page load that calls several
/// endpoints at once runs each Azure query only once.
/// </summary>
public class CacheRoutingTests : IClassFixture<TestApp>
{
    private const string LastLogQuery = "LastLog = max(TimeGenerated) by Type";

    private readonly TestApp _app;

    public CacheRoutingTests(TestApp app)
    {
        _app = app;
    }

    [Fact]
    public async Task ParallelFirstLoad_RunsEachQueryOnce()
    {
        using var fake = new FakeAzureApp(_app);
        var client = fake.CreateViewerClient();

        // What the Dashboard and the Collection Rules page request at the same time.
        var responses = await Task.WhenAll(
            client.GetAsync($"/api/analyticsrules?{FakeAzureApp.WorkspaceQuery}"),
            client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}"),
            client.GetAsync($"/api/dataconnectors?{FakeAzureApp.WorkspaceQuery}"),
            client.GetAsync($"/api/dcr?{FakeAzureApp.WorkspaceQuery}"),
            client.GetAsync($"/api/dcr?{FakeAzureApp.WorkspaceQuery}"));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, fake.Logs.CountCalls(LastLogQuery));
        Assert.Equal(1, fake.Arm.CountCalls("/tables"));
        Assert.Equal(1, fake.Arm.CountCalls("/dataCollectionRules"));
        Assert.Equal(1, fake.Arm.CountCalls("/alertRules"));
    }

    [Fact]
    public async Task CachedData_IsServedWithoutNewQueries()
    {
        using var fake = new FakeAzureApp(_app);
        var client = fake.CreateViewerClient();

        await client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}");
        await WaitForRefreshAsync(fake);
        var callsAfterFirstLoad = fake.Logs.Calls.Count;

        await client.GetAsync($"/api/dataconnectors?{FakeAzureApp.WorkspaceQuery}");
        await client.GetAsync($"/api/dcr?{FakeAzureApp.WorkspaceQuery}");

        Assert.Equal(callsAfterFirstLoad, fake.Logs.Calls.Count);
    }

    [Fact]
    public async Task SentinelHealth_ParallelRequestsShareOneQuery()
    {
        using var fake = new FakeAzureApp(_app);
        var client = fake.CreateViewerClient();

        var responses = await Task.WhenAll(
            client.GetAsync($"/api/sentinelhealth?{FakeAzureApp.WorkspaceQuery}&days=7"),
            client.GetAsync($"/api/sentinelhealth?{FakeAzureApp.WorkspaceQuery}&days=7"));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(1, fake.Logs.CountCalls("_SentinelHealth() | take 1"));
    }

    [Fact]
    public async Task DcrPageDisabled_NoDcrIsReadAndTheEndpointReturns404()
    {
        using var fake = new FakeAzureApp(_app, enableDcrPage: false);
        var client = fake.CreateViewerClient();

        var dashboard = await Task.WhenAll(
            client.GetAsync($"/api/analyticsrules?{FakeAzureApp.WorkspaceQuery}"),
            client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}"),
            client.GetAsync($"/api/dataconnectors?{FakeAzureApp.WorkspaceQuery}"));
        var dcr = await client.GetAsync($"/api/dcr?{FakeAzureApp.WorkspaceQuery}");
        await WaitForRefreshAsync(fake);

        Assert.All(dashboard, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(HttpStatusCode.NotFound, dcr.StatusCode);
        Assert.Equal(0, fake.Arm.CountCalls("/dataCollectionRules"));
    }

    // Phase 2 (incidents) finishes after the endpoints return.
    private static async Task WaitForRefreshAsync(FakeAzureApp fake)
    {
        for (var i = 0; i < 100 && fake.Logs.CountCalls("SecurityIncident") == 0; i++)
            await Task.Delay(50);
        await Task.Delay(100);
    }
}
