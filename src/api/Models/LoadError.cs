using System.Net;
using Azure;
using Azure.Identity;

namespace SentinelWatcher.Api.Models;

/// <summary>
/// Why a section could not be loaded. Sent to the browser instead of empty data, so a failure is
/// never shown as zeros or as "all good". The message is generic on purpose: details (which can
/// contain tenant or resource IDs) are only in the server log.
/// </summary>
public sealed record LoadError(string Code, string Message)
{
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string AccessDenied = "AccessDenied";
    public const string NotFound = "NotFound";
    public const string Timeout = "Timeout";
    public const string AzureRequestFailed = "AzureRequestFailed";

    public static LoadError From(Exception exception)
    {
        var ex = exception is AggregateException { InnerException: not null } agg ? agg.InnerException : exception;

        if (ex is AuthenticationFailedException or CredentialUnavailableException)
            return new(AuthenticationFailed,
                "Could not get an Azure token for the app's identity. See the server log.");

        var status = ex switch
        {
            RequestFailedException rfe => (HttpStatusCode)rfe.Status,
            HttpRequestException { StatusCode: { } code } => code,
            _ => (HttpStatusCode?)null,
        };

        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(AccessDenied,
                "Azure denied access. Check the role assignments of the app's identity. See the server log."),
            HttpStatusCode.NotFound => new(NotFound, "An Azure resource was not found. See the server log."),
            _ when ex is TimeoutException or TaskCanceledException => new(Timeout,
                "Azure did not answer in time. See the server log."),
            _ => new(AzureRequestFailed, "Could not load data from Azure. See the server log."),
        };
    }
}
