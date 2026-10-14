using System.Collections.Concurrent;
using Azure.Core;
using Azure.Identity;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

/// <summary>
/// The single Azure identity the backend uses for every call. Viewers never need Azure roles:
/// all reads run as this identity.
/// </summary>
public static class AzureCredentialFactory
{
    public static TokenCredential Create(SentinelOptions options, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(options.ManagedIdentityClientId))
        {
            logger.LogInformation("Using user-assigned managed identity {ClientId} for Azure calls",
                options.ManagedIdentityClientId);
            return new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
        }

        // Local development: Azure CLI (az login), Visual Studio, environment variables and so on.
        logger.LogInformation(
            "Sentinel:ManagedIdentityClientId is not set. Using DefaultAzureCredential (for example az login) for Azure calls");
        return new DefaultAzureCredential();
    }
}

/// <summary>
/// Caches access tokens per scope until shortly before they expire. Azure SDK clients cache
/// tokens themselves; this covers the raw ARM calls made with HttpClient.
/// </summary>
public sealed class AzureTokenProvider
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly TokenCredential _credential;
    private readonly ConcurrentDictionary<string, AccessToken> _tokens = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public AzureTokenProvider(TokenCredential credential)
    {
        _credential = credential;
    }

    public async Task<string> GetTokenAsync(string scope, CancellationToken cancellationToken = default)
    {
        if (TryGetCached(scope, out var cached)) return cached;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(scope, out cached)) return cached;

            var token = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken);
            _tokens[scope] = token;
            return token.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    private bool TryGetCached(string scope, out string token)
    {
        if (_tokens.TryGetValue(scope, out var cached) && cached.ExpiresOn - RefreshMargin > DateTimeOffset.UtcNow)
        {
            token = cached.Token;
            return true;
        }

        token = string.Empty;
        return false;
    }
}
