using System.Net;
using System.Net.Http.Json;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Tests;

public class WorkspaceAllowListTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public WorkspaceAllowListTests(TestApp app)
    {
        _app = app;
    }

    [Fact]
    public async Task Workspaces_ReturnsOnlyConfiguredWorkspaces()
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);

        var workspaces = await client.GetFromJsonAsync<List<WorkspaceInfo>>("/api/workspaces");

        var workspace = Assert.Single(workspaces!);
        Assert.Equal(TestApp.AllowedWorkspace, workspace.ResourceId);
        Assert.Equal("law-allowed", workspace.WorkspaceName);
        Assert.Equal("rg-sentinel", workspace.ResourceGroup);
    }

    [Theory]
    [InlineData("/api/analyticsrules")]
    [InlineData("/api/ingestion")]
    [InlineData("/api/dataconnectors")]
    [InlineData("/api/dcr")]
    [InlineData("/api/sentinelhealth")]
    public async Task UnlistedWorkspace_Returns403(string path)
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);

        var response = await client.GetAsync($"{path}?workspace={Uri.EscapeDataString(TestApp.UnlistedWorkspace)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UnlistedWorkspace_RefreshReturns403()
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);

        var response = await client.PostAsync(
            $"/api/analyticsrules/refresh?workspace={Uri.EscapeDataString(TestApp.UnlistedWorkspace)}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/analyticsrules")]
    [InlineData("/api/dcr")]
    [InlineData("/api/sentinelhealth")]
    public async Task MissingWorkspace_Returns400(string path)
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OverlongWorkspace_Returns400()
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);
        var overlong = TestApp.AllowedWorkspace + new string('a', 600);

        var response = await client.GetAsync($"/api/dcr?workspace={Uri.EscapeDataString(overlong)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sentinel/providers/Microsoft.OperationalInsights/workspaces/law-allowed' | union *")]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sentinel/providers/Microsoft.OperationalInsights/workspaces/law-allowed/../law-other")]
    [InlineData("law-allowed")]
    public async Task ManipulatedWorkspace_Returns403(string workspace)
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);

        var response = await client.GetAsync($"/api/dcr?workspace={Uri.EscapeDataString(workspace)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
