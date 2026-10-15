using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Endpoints;

/// <summary>
/// Serves the sign-in settings the SPA needs at runtime, so a single client build
/// works for every tenant. Contains no secrets: client ID, tenant ID and scope are
/// public values that end up in the browser anyway.
/// </summary>
public static class ClientConfigEndpoint
{
    public const string Path = "/config.json";

    public static IEndpointRouteBuilder MapClientConfig(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, (IConfiguration config, IOptions<SentinelOptions> sentinel, HttpContext http) =>
        {
            http.Response.Headers.CacheControl = "no-store";

            var instance = (config["AzureAd:Instance"] ?? "https://login.microsoftonline.com/").TrimEnd('/');
            var tenantId = config["AzureAd:TenantId"];
            var clientId = config["AzureAd:ClientId"];

            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId))
            {
                return Results.Json(
                    new { message = "AzureAd:TenantId and AzureAd:ClientId are not configured on the server." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new ClientConfig(
                ClientId: clientId,
                TenantId: tenantId,
                Authority: $"{instance}/{tenantId}",
                ApiScope: $"api://{clientId}/access_as_user",
                Features: new ClientFeatures(DcrPage: sentinel.Value.EnableDcrPage)));
        }).AllowAnonymous();

        return app;
    }
}

public record ClientConfig(string ClientId, string TenantId, string Authority, string ApiScope, ClientFeatures Features);

/// <summary>Pages that can be turned off in configuration.</summary>
public record ClientFeatures(bool DcrPage);
