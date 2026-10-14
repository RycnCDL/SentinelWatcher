using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SentinelWatcher.Api.Services;

/// <summary>
/// Minimal Azure Resource Manager GET client. Paths are built only from configured
/// workspace values (see WorkspaceRegistry), never from request input.
/// </summary>
public class ArmRestClient
{
    public const string BaseUrl = "https://management.azure.com";

    private readonly HttpClient _httpClient;
    private readonly UserAzureClientFactory _clientFactory;
    private readonly ILogger<ArmRestClient> _logger;

    public ArmRestClient(HttpClient httpClient, UserAzureClientFactory clientFactory, ILogger<ArmRestClient> logger)
    {
        _httpClient = httpClient;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>Returns true for 200, false for 404, and throws for any other status.</summary>
    public async Task<bool> ExistsAsync(string path, string apiVersion, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(BuildUrl(path, apiVersion), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        response.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>GETs a collection and follows nextLink pages on management.azure.com.</summary>
    public async Task<List<JsonElement>> GetAllPagesAsync(string path, string apiVersion, CancellationToken cancellationToken = default)
    {
        var items = new List<JsonElement>();
        string? url = BuildUrl(path, apiVersion);

        while (url is not null)
        {
            using var response = await SendAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                    items.Add(item.Clone());
            }

            url = doc.RootElement.TryGetProperty("nextLink", out var nextLink) ? nextLink.GetString() : null;

            // Never send the bearer token anywhere other than Azure Resource Manager.
            if (url is not null && !url.StartsWith(BaseUrl + "/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Ignoring unexpected nextLink host in ARM response");
                url = null;
            }
        }

        return items;
    }

    private static string BuildUrl(string path, string apiVersion)
    {
        if (!path.StartsWith("/subscriptions/", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("ARM paths must start with /subscriptions/.", nameof(path));

        return $"{BaseUrl}{path}?api-version={Uri.EscapeDataString(apiVersion)}";
    }

    private async Task<HttpResponseMessage> SendAsync(string url, CancellationToken cancellationToken)
    {
        var token = await _clientFactory.GetArmAccessTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _httpClient.SendAsync(request, cancellationToken);
    }
}
