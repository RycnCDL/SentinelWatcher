using Azure.Core;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SentinelWatcher.Api.Auth;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests.Fakes;

/// <summary>
/// The full API in memory with Log Analytics, ARM and the token credential replaced by fakes.
/// </summary>
public sealed class FakeAzureApp : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public FakeAzureApp(
        TestApp app,
        Func<string, LogsQueryResult>? logs = null,
        Func<string, string?>? arm = null,
        bool enableDcrPage = true)
    {
        Logs = new FakeLogsQueryClient(logs ?? (_ => LogsResults.Empty()));
        Arm = new FakeArmHandler(arm ?? EmptyCollections);

        _factory = app.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sentinel:EnableDcrPage", enableDcrPage.ToString());
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<LogsQueryClient>(Logs);
                services.AddSingleton<TokenCredential>(new FakeTokenCredential());
                services.AddHttpClient(nameof(ArmRestClient)).ConfigurePrimaryHttpMessageHandler(() => Arm);
            });
        });
    }

    public FakeLogsQueryClient Logs { get; }

    public FakeArmHandler Arm { get; }

    public IServiceProvider Services => _factory.Services;

    /// <summary>A client signed in with the Viewer role.</summary>
    public HttpClient CreateViewerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, "test-user");
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, AppRoles.Viewer);
        return client;
    }

    public static string WorkspaceQuery => $"workspace={Uri.EscapeDataString(TestApp.AllowedWorkspace)}";

    /// <summary>Every ARM collection is empty; single resources (solution, onboarding state) are 404.</summary>
    public static string? EmptyCollections(string path) =>
        path.EndsWith("/alertRules", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/dataConnectors", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/tables", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith("/dataCollectionRules", StringComparison.OrdinalIgnoreCase)
            ? """{ "value": [] }"""
            : null;

    public void Dispose() => _factory.Dispose();
}
