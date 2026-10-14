using Microsoft.Identity.Web;
using SentinelWatcher.Api.Endpoints;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<CacheSettingsConfig>(
    builder.Configuration.GetSection("CacheSettings"));
builder.Services.Configure<SentinelOptions>(
    builder.Configuration.GetSection(SentinelOptions.SectionName));

// Workspace allow-list (Sentinel:Workspaces): the only workspaces the app reads.
builder.Services.AddSingleton<WorkspaceRegistry>();

// Azure AD authentication with OBO flow
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration)
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// Azure client factory (creates per-request clients using user's OBO token)
builder.Services.AddScoped<UserAzureClientFactory>();
builder.Services.AddHttpClient<ArmRestClient>();

builder.Services.AddScoped<IWorkspaceStatusProvider, SentinelStatusProvider>();
builder.Services.AddScoped<SentinelService>();
builder.Services.AddScoped<LogAnalyticsService>();
builder.Services.AddScoped<DataCollectionRuleService>();
builder.Services.AddScoped<WorkspaceService>();

// Per-workspace cache manager (singleton, shared across requests)
builder.Services.AddSingleton<WorkspaceCacheManager>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Validate the workspace allow-list at startup so configuration errors fail fast.
app.Services.GetRequiredService<WorkspaceRegistry>();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document at /openapi/v1.json (development only).
    app.MapOpenApi();
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
app.MapFallbackToFile("index.html");

app.Run();

// Exposed for integration tests (WebApplicationFactory<Program>).
public partial class Program;
