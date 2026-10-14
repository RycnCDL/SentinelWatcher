using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

/// <summary>
/// Hosts the API in memory with a fake authentication scheme and no Azure calls.
/// </summary>
public sealed class TestApp : WebApplicationFactory<Program>
{
    public const string AllowedWorkspace =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sentinel" +
        "/providers/Microsoft.OperationalInsights/workspaces/law-allowed";

    public const string UnlistedWorkspace =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-other" +
        "/providers/Microsoft.OperationalInsights/workspaces/law-unlisted";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("AzureAd:Instance", "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId", "11111111-1111-1111-1111-111111111111");
        builder.UseSetting("AzureAd:ClientId", "22222222-2222-2222-2222-222222222222");
        builder.UseSetting("Sentinel:Workspaces:0", AllowedWorkspace);

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
            });

            services.AddSingleton<IWorkspaceStatusProvider, FakeStatusProvider>();
        });
    }

    /// <summary>Creates a client that is signed in with the given app roles (none = signed in without roles).</summary>
    public HttpClient CreateSignedInClient(params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "test-user");
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(",", roles));
        return client;
    }

    private sealed class FakeStatusProvider : IWorkspaceStatusProvider
    {
        public Task<bool> IsSentinelEnabledAsync(WorkspaceContext workspace, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }
}

/// <summary>
/// Authenticates a request when the X-Test-User header is present and adds a "roles"
/// claim for each value in X-Test-Roles, matching the shape of Entra ID access tokens.
/// </summary>
public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string UserHeader = "X-Test-User";
    public const string RolesHeader = "X-Test-Roles";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var user))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new("name", user.ToString()) };
        if (Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            claims.AddRange(roles.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(role => new Claim("roles", role)));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, "name", "roles");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
