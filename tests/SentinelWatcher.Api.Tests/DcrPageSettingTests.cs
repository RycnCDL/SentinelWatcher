using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SentinelWatcher.Api.Auth;

namespace SentinelWatcher.Api.Tests;

public class DcrPageSettingTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public DcrPageSettingTests(TestApp app)
    {
        _app = app;
    }

    [Fact]
    public async Task ConfigJson_ReportsTheDcrPageAsEnabledByDefault()
    {
        var json = await _app.CreateClient().GetStringAsync("/config.json");

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("features").GetProperty("dcrPage").GetBoolean());
    }

    [Fact]
    public async Task WhenDisabled_DcrEndpointReturns404AndConfigJsonSaysSo()
    {
        using var app = _app.WithWebHostBuilder(b => b.UseSetting("Sentinel:EnableDcrPage", "false"));

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "test-user");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, AppRoles.Viewer);

        var response = await client.GetAsync(
            $"/api/dcr?workspace={Uri.EscapeDataString(TestApp.AllowedWorkspace)}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var doc = JsonDocument.Parse(await client.GetStringAsync("/config.json"));
        Assert.False(doc.RootElement.GetProperty("features").GetProperty("dcrPage").GetBoolean());
    }

    [Fact]
    public async Task WhenDisabled_TheViewerRoleIsStillRequired()
    {
        using var app = _app.WithWebHostBuilder(b => b.UseSetting("Sentinel:EnableDcrPage", "false"));

        var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "test-user");

        var response = await client.GetAsync(
            $"/api/dcr?workspace={Uri.EscapeDataString(TestApp.AllowedWorkspace)}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
