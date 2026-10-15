using Azure.Core;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Monitor.Query.Logs;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using SentinelWatcher.Api.Auth;
using SentinelWatcher.Api.Endpoints;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<CacheSettingsConfig>(
    builder.Configuration.GetSection("CacheSettings"));
builder.Services.Configure<SentinelOptions>(
    builder.Configuration.GetSection(SentinelOptions.SectionName));
builder.Services.Configure<ConnectorHealthOptions>(
    builder.Configuration.GetSection(ConnectorHealthOptions.SectionName));
builder.Services.AddSingleton<ConnectorThresholds>();

// Workspace allow-list (Sentinel:Workspaces): the only workspaces the app reads.
builder.Services.AddSingleton<WorkspaceRegistry>();

// Sign-in: validate Entra ID access tokens issued for this app. The API never calls
// Azure on behalf of the user, so no client secret is needed.
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration);

// Authorization: every endpoint requires the SentinelWatcher.Viewer app role, including
// endpoints without an [Authorize] attribute (fallback policy). Only /config.json and the
// SPA files are anonymous. "Assignment required" on the enterprise app additionally keeps
// unassigned users from signing in at all.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppRoles.ViewerPolicy, AppRoles.BuildViewerPolicy())
    .SetDefaultPolicy(AppRoles.BuildViewerPolicy())
    .SetFallbackPolicy(AppRoles.BuildViewerPolicy());

// Azure access: one identity for the whole app (user-assigned managed identity in Azure,
// DefaultAzureCredential such as az login for local development).
builder.Services.AddSingleton<TokenCredential>(sp => AzureCredentialFactory.Create(
    sp.GetRequiredService<IOptions<SentinelOptions>>().Value,
    sp.GetRequiredService<ILoggerFactory>().CreateLogger("SentinelWatcher.Azure")));
builder.Services.AddSingleton<AzureTokenProvider>();
builder.Services.AddSingleton(sp => new LogsQueryClient(sp.GetRequiredService<TokenCredential>()));
builder.Services.AddHttpClient(nameof(ArmRestClient));
builder.Services.AddSingleton<ArmRestClient>();

// All Azure-calling services are singletons: they hold no per-request state, which lets
// the cache refresh run in the background after the triggering request has finished.
builder.Services.AddSingleton<IWorkspaceStatusProvider, SentinelStatusProvider>();
builder.Services.AddSingleton<SentinelService>();
builder.Services.AddSingleton<LogAnalyticsService>();
builder.Services.AddSingleton<DataCollectionRuleService>();
builder.Services.AddSingleton<TableTierService>();
builder.Services.AddSingleton<WorkspaceService>();

// Per-workspace cache, shared by all viewers (everyone sees data read by the same identity).
builder.Services.AddSingleton<WorkspaceCacheManager>();
builder.Services.AddHostedService<CacheEvictionService>();

// Telemetry: requests, dependencies and logs go to Application Insights only when a
// connection string is configured (APPLICATIONINSIGHTS_CONNECTION_STRING, set by infra/main.bicep).
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Validate the workspace allow-list at startup so configuration errors fail fast, and create the
// Azure credential now so the log says at startup which one is used.
app.Services.GetRequiredService<WorkspaceRegistry>();
app.Services.GetRequiredService<TokenCredential>();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document at /openapi/v1.json (development only).
    app.MapOpenApi().AllowAnonymous();
}

// The SPA is served from wwwroot on the same origin as the API, so no CORS is needed.
// In local development the Vite dev server proxies /api and /config.json to this app.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapClientConfig();
app.MapControllers();

// Unknown API routes return 404 instead of falling through to the SPA.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
