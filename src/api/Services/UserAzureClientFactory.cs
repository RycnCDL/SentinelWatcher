using Azure.Core;
using Azure.Monitor.Query;
using Microsoft.Identity.Web;

namespace SentinelWatcher.Api.Services;

/// <summary>
/// Creates Azure clients using the signed-in user's token via OBO flow.
/// Registered as Scoped: one instance per HTTP request.
/// </summary>
public class UserAzureClientFactory
{
    private readonly ITokenAcquisition _tokenAcquisition;
    private readonly ILogger<UserAzureClientFactory> _logger;

    private static readonly string[] ArmScopes = { "https://management.azure.com/.default" };
    private static readonly string[] LogAnalyticsScopes = { "https://api.loganalytics.io/.default" };

    public UserAzureClientFactory(
        ITokenAcquisition tokenAcquisition,
        ILogger<UserAzureClientFactory> logger)
    {
        _tokenAcquisition = tokenAcquisition;
        _logger = logger;
    }

    public async Task<LogsQueryClient> CreateLogsQueryClientAsync()
    {
        var token = await _tokenAcquisition.GetAccessTokenForUserAsync(LogAnalyticsScopes);
        var credential = new StaticTokenCredential(token);
        return new LogsQueryClient(credential);
    }

    public async Task<string> GetArmAccessTokenAsync()
    {
        return await _tokenAcquisition.GetAccessTokenForUserAsync(ArmScopes);
    }
}

/// <summary>
/// Simple TokenCredential wrapper that returns a pre-acquired token string.
/// Used to create Azure SDK clients from an OBO-acquired token.
/// </summary>
public class StaticTokenCredential : TokenCredential
{
    private readonly string _token;

    public StaticTokenCredential(string token)
    {
        _token = token;
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new AccessToken(_token, DateTimeOffset.UtcNow.AddMinutes(55));
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new ValueTask<AccessToken>(new AccessToken(_token, DateTimeOffset.UtcNow.AddMinutes(55)));
    }
}
