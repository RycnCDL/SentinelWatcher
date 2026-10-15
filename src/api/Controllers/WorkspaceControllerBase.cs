using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Auth;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

/// <summary>
/// Base for endpoints that read one workspace. The client passes the workspace resource ID
/// in the "workspace" query parameter; it must match the configured allow-list exactly.
/// </summary>
[ApiController]
[Authorize(Policy = AppRoles.ViewerPolicy)]
public abstract class WorkspaceControllerBase : ControllerBase
{
    private readonly WorkspaceRegistry _registry;

    protected WorkspaceControllerBase(WorkspaceRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Answers with a cached section: its data (200, possibly older data marked with RefreshError),
    /// the error of the latest refresh when there is no data (502), or "still loading" (503).
    /// A failure is never answered with empty data, which the UI would show as zeros.
    /// </summary>
    protected ActionResult Section<T>(T? data, LoadError? error) where T : class
    {
        if (data is not null) return Ok(data);
        if (error is not null) return SectionError(error);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            new { message = "Data is still loading. Please try again shortly." });
    }

    /// <summary>502 with the error, in the shape the client reads: { "error": { "code", "message" } }.</summary>
    protected ActionResult SectionError(LoadError error) =>
        StatusCode(StatusCodes.Status502BadGateway, new { error });

    /// <summary>
    /// Resolves the requested workspace. Returns null and sets <paramref name="error"/> to
    /// 400 (missing or malformed) or 403 (not on the allow-list).
    /// </summary>
    protected WorkspaceContext? ResolveWorkspace(string? workspace, out ActionResult? error)
    {
        switch (_registry.TryResolve(workspace, out var resolved))
        {
            case WorkspaceLookupStatus.Found:
                error = null;
                return resolved;
            case WorkspaceLookupStatus.Missing:
                error = BadRequest(new { message = "The 'workspace' query parameter is required." });
                return null;
            case WorkspaceLookupStatus.Invalid:
                error = BadRequest(new { message = "The 'workspace' query parameter is not a valid workspace resource ID." });
                return null;
            default:
                error = StatusCode(StatusCodes.Status403Forbidden,
                    new { message = "This workspace is not configured in SentinelWatcher." });
                return null;
        }
    }
}
