using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SentinelWatcher.Api.Auth;

namespace SentinelWatcher.Api.Tests;

public class AppRoleTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public AppRoleTests(TestApp app)
    {
        _app = app;
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        var response = await _app.CreateClient().GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignedInWithoutRole_Returns403()
    {
        var response = await _app.CreateSignedInClient().GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("SentinelWatcher.Admin")]
    [InlineData("sentinelwatcher.viewer")]
    [InlineData("Viewer")]
    public async Task SignedInWithOtherRole_Returns403(string role)
    {
        var response = await _app.CreateSignedInClient(role).GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SignedInWithViewerRole_Returns200()
    {
        var response = await _app.CreateSignedInClient(TestRoles.Viewer).GetAsync("/api/workspaces");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EveryApiEndpoint_RequiresViewerRole()
    {
        var endpoints = _app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.NotEmpty(endpoints);

        var client = _app.CreateSignedInClient();
        var workspace = Uri.EscapeDataString(TestApp.AllowedWorkspace);

        foreach (var endpoint in endpoints)
        {
            var path = "/" + endpoint.RoutePattern.RawText!.Replace("{**path}", "x");
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" };

            foreach (var method in methods)
            {
                var request = new HttpRequestMessage(new HttpMethod(method), $"{path}?workspace={workspace}");
                var response = await client.SendAsync(request);

                Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                    $"{method} {path} returned {(int)response.StatusCode} for a user without the Viewer role.");
            }
        }
    }

    [Fact]
    public async Task ClientConfig_IsAnonymous()
    {
        var response = await _app.CreateClient().GetAsync("/config.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("roles", AppRoles.Viewer, true)]
    [InlineData(ClaimTypes.Role, AppRoles.Viewer, true)]
    [InlineData("scp", AppRoles.Viewer, false)]
    [InlineData("roles", "SentinelWatcher.Viewer.Extra", false)]
    public async Task ViewerPolicy_ChecksRoleClaim(string claimType, string value, bool expected)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(claimType, value) }, "Test"));
        var authorization = _app.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(user, null, AppRoles.ViewerPolicy);

        Assert.Equal(expected, result.Succeeded);
    }
}
