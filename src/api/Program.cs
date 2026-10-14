using Microsoft.Identity.Web;
using SentinelWatcher.Api.Endpoints;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<CacheSettingsConfig>(
    builder.Configuration.GetSection("CacheSettings"));

// Azure AD authentication with OBO flow
builder.Services.AddMicrosoftIdentityWebApiAuthentication(builder.Configuration)
    .EnableTokenAcquisitionToCallDownstreamApi()
    .AddInMemoryTokenCaches();

// Azure client factory (creates per-request clients using user's OBO token)
builder.Services.AddScoped<UserAzureClientFactory>();

// Resource Graph: single ARG round-trip replaces N+1 ARM REST calls for config data
builder.Services.AddHttpClient<ResourceGraphService>();

// Services (HttpClient-typed registrations handle their own HttpClient)
builder.Services.AddHttpClient<SentinelService>();
builder.Services.AddScoped<SentinelService>();
builder.Services.AddScoped<LogAnalyticsService>();
builder.Services.AddScoped<DataCollectionRuleService>();
builder.Services.AddScoped<WorkspaceService>();

// Per-workspace cache manager (singleton, shared across requests)
builder.Services.AddSingleton<WorkspaceCacheManager>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

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
