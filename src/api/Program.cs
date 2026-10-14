using Microsoft.Identity.Web;
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

// CORS (configurable via appsettings.json)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
