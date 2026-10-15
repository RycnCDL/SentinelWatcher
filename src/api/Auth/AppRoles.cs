using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace SentinelWatcher.Api.Auth;

/// <summary>
/// Entra ID app roles defined on the SentinelWatcher app registration.
/// </summary>
public static class AppRoles
{
    /// <summary>App role value that grants read access to SentinelWatcher.</summary>
    public const string Viewer = "SentinelWatcher.Viewer";

    /// <summary>Authorization policy that requires the Viewer app role.</summary>
    public const string ViewerPolicy = "SentinelWatcher.Viewer";

    /// <summary>
    /// Requires a signed-in user whose access token carries the Viewer role. Entra ID puts app
    /// roles in the "roles" claim; ClaimTypes.Role is accepted too in case claims are mapped.
    /// </summary>
    public static AuthorizationPolicy BuildViewerPolicy() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Claims.Any(claim =>
                (claim.Type == "roles" || claim.Type == ClaimTypes.Role) &&
                string.Equals(claim.Value, Viewer, StringComparison.Ordinal)))
            .Build();
}
