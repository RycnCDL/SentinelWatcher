using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

/// <summary>
/// Base for endpoints that read one workspace. The client passes the workspace resource ID
/// in the "workspace" query parameter; it must match the configured allow-list exactly.
/// </summary>
[ApiController]
[Authorize]
public abstract class WorkspaceControllerBase : ControllerBase
{
    private readonly WorkspaceRegistry _registry;

    protected WorkspaceControllerBase(WorkspaceRegistry registry)
    {
        _registry = registry;
    }

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
