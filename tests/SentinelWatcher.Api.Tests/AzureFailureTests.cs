using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.Query.Logs.Models;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Tests.Fakes;

namespace SentinelWatcher.Api.Tests;

/// <summary>
/// When Azure calls fail, the API returns the error instead of empty data that the UI would show
/// as zeros or as "all good", and retries soon.
/// </summary>
public class AzureFailureTests : IClassFixture<TestApp>
{
    private static readonly string[] SectionEndpoints =
    {
        "/api/analyticsrules", "/api/ingestion", "/api/dataconnectors", "/api/dcr", "/api/sentinelhealth",
    };

    private readonly TestApp _app;

    public AzureFailureTests(TestApp app)
    {
        _app = app;
    }

    /// <summary>The failure from the local run: no token for any Azure call.</summary>
    private static Exception TokenFailure() =>
        new AuthenticationFailedException("ManagedIdentityCredential authentication failed: All Managed Identity sources are unavailable.");

    [Fact]
    public async Task WhenNoTokenCanBeAcquired_EverySectionReturnsTheError()
    {
        using var fake = new FakeAzureApp(_app,
            logs: _ => throw TokenFailure(),
            credential: new SwitchableCredential { Failing = true });
        var client = fake.CreateViewerClient();

        foreach (var endpoint in SectionEndpoints)
        {
            var response = await client.GetAsync($"{endpoint}?{FakeAzureApp.WorkspaceQuery}");

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var error = await ReadErrorAsync(response);
            Assert.Equal(LoadError.AuthenticationFailed, error.Code);
            Assert.Contains("server log", error.Message);
        }

        // The Sentinel status is unknown, not "not enabled".
        var workspaces = await client.GetFromJsonAsync<List<WorkspaceInfo>>("/api/workspaces");
        Assert.Null(Assert.Single(workspaces!).SentinelEnabled);
    }

    [Fact]
    public async Task WhenAzureDeniesAccess_TheErrorSaysSo()
    {
        using var fake = new FakeAzureApp(_app,
            logs: _ => throw new RequestFailedException(403, "AuthorizationFailed"),
            arm: _ => null);
        var client = fake.CreateViewerClient();

        var response = await client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(LoadError.AccessDenied, (await ReadErrorAsync(response)).Code);
    }

    [Fact]
    public async Task FailedSections_AreRetriedSoon_AndRecover()
    {
        var failing = true;
        var credential = new SwitchableCredential { Failing = true };
        using var fake = new FakeAzureApp(_app,
            logs: _ => failing ? throw TokenFailure() : LogsResults.Empty(),
            credential: credential,
            settings: new Dictionary<string, string> { ["CacheSettings:ErrorRetrySeconds"] = "1" });
        var client = fake.CreateViewerClient();

        var first = await client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}");
        Assert.Equal(HttpStatusCode.BadGateway, first.StatusCode);

        // The cause is fixed (for example az login). With a 10 minute cache the failure would stay;
        // failed sections are retried after ErrorRetrySeconds.
        failing = false;
        credential.Failing = false;

        HttpResponseMessage? recovered = null;
        for (var i = 0; i < 60; i++)
        {
            await Task.Delay(100);
            recovered = await client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}");
            if (recovered.StatusCode == HttpStatusCode.OK) break;
        }

        Assert.Equal(HttpStatusCode.OK, recovered!.StatusCode);
        var body = await recovered.Content.ReadFromJsonAsync<IngestionResponse>();
        Assert.Null(body!.RefreshError);
    }

    [Fact]
    public async Task FailedRefresh_KeepsOlderDataAndMarksIt()
    {
        var failing = false;
        using var fake = new FakeAzureApp(_app, logs: _ => failing ? throw TokenFailure() : LogsResults.Empty());
        var client = fake.CreateViewerClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}")).StatusCode);
        await WaitUntilRefreshedAsync(fake);

        failing = true;
        await client.PostAsync($"/api/analyticsrules/refresh?{FakeAzureApp.WorkspaceQuery}", null);

        var response = await client.GetAsync($"/api/ingestion?{FakeAzureApp.WorkspaceQuery}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<IngestionResponse>();
        Assert.Equal(LoadError.AuthenticationFailed, body!.RefreshError?.Code);
    }

    [Fact]
    public async Task IncidentQueryFailure_KeepsTheRuleListAndReportsTheIncidentError()
    {
        using var fake = new FakeAzureApp(_app,
            logs: q => q.Contains("SecurityIncident") ? throw new RequestFailedException(500, "boom") : LogsResults.Empty());
        var client = fake.CreateViewerClient();

        AnalyticsRulesResponse? body = null;
        for (var i = 0; i < 60 && body?.IncidentsError is null; i++)
        {
            body = await client.GetFromJsonAsync<AnalyticsRulesResponse>($"/api/analyticsrules?{FakeAzureApp.WorkspaceQuery}");
            if (body?.IncidentsError is null) await Task.Delay(50);
        }

        Assert.NotNull(body!.IncidentsError);
        Assert.False(body.IncidentsLoaded);
    }

    [Fact]
    public async Task MissingSentinelHealthTable_IsNotEnabled_NotAnError()
    {
        using var fake = new FakeAzureApp(_app, logs: q => q.Contains("_SentinelHealth()")
            ? throw new RequestFailedException(400, "Failed to resolve table or column expression named 'SentinelHealth'")
            : LogsResults.Empty());
        var client = fake.CreateViewerClient();

        var body = await client.GetFromJsonAsync<SentinelHealthResponse>($"/api/sentinelhealth?{FakeAzureApp.WorkspaceQuery}");

        Assert.False(body!.HealthMonitoringEnabled);
    }

    private static async Task<LoadError> ReadErrorAsync(HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = doc.RootElement.GetProperty("error");
        return new LoadError(error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);
    }

    private static async Task WaitUntilRefreshedAsync(FakeAzureApp fake)
    {
        for (var i = 0; i < 100 && fake.Logs.CountCalls("SecurityIncident") == 0; i++)
            await Task.Delay(50);
        await Task.Delay(100);
    }

    private sealed class SwitchableCredential : TokenCredential
    {
        public bool Failing { get; set; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            Failing ? throw TokenFailure() : new AccessToken("fake-token", DateTimeOffset.UtcNow.AddMinutes(10));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }
}
