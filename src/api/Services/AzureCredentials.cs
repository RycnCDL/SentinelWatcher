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
            logger.LogInformation("Azure credential: user-assigned managed identity {ClientId}",
                options.ManagedIdentityClientId);
            return new ManagedIdentityCredential(
                ManagedIdentityId.FromUserAssignedClientId(options.ManagedIdentityClientId));
        }

        // Local development. No managed identity or workload identity: on a dev machine the
        // managed identity endpoint does not exist, and DefaultAzureCredential can fail there
        // instead of moving on to the Azure CLI.
        var chain = new DeveloperCredentialChain(DeveloperSources(), logger);
        logger.LogInformation(
            "Azure credential: Sentinel:ManagedIdentityClientId is not set, using developer sign-in in this order: {Sources}",
            string.Join(", ", chain.SourceNames));
        return chain;
    }

    /// <summary>
    /// Developer credentials in the order they are tried: Azure CLI (az login) first, then
    /// Visual Studio, Visual Studio Code and Azure PowerShell.
    /// </summary>
    public static IReadOnlyList<TokenCredential> DeveloperSources() => new TokenCredential[]
    {
        new AzureCliCredential(),
        new VisualStudioCredential(),
        new VisualStudioCodeCredential(),
        new AzurePowerShellCredential(),
    };
}

/// <summary>
/// Tries each credential in order, like ChainedTokenCredential: a source that is not available
/// (for example not signed in) is skipped, any other error stops the chain. Logs once which source
/// delivered the first token.
/// </summary>
public sealed class DeveloperCredentialChain : TokenCredential
{
    private readonly IReadOnlyList<TokenCredential> _sources;
    private readonly ILogger _logger;
    private int _loggedSource;

    public DeveloperCredentialChain(IReadOnlyList<TokenCredential> sources, ILogger logger)
    {
        _sources = sources;
        _logger = logger;
    }

    public IReadOnlyList<TokenCredential> Sources => _sources;

    public IEnumerable<string> SourceNames => _sources.Select(s => s.GetType().Name);

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var unavailable = new List<CredentialUnavailableException>();
        foreach (var source in _sources)
        {
            try
            {
                var token = await source.GetTokenAsync(requestContext, cancellationToken);
                if (Interlocked.Exchange(ref _loggedSource, 1) == 0)
                    _logger.LogInformation("Azure credential: signed in with {Source}", source.GetType().Name);
                return token;
            }
            catch (CredentialUnavailableException ex)
            {
                unavailable.Add(ex);
            }
        }

        throw new CredentialUnavailableException(
            "No developer sign-in is available. Run 'az login' (or sign in to Visual Studio, VS Code or Azure PowerShell). " +
            string.Join(" ", unavailable.Select(e => e.Message)),
            new AggregateException(unavailable));
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
