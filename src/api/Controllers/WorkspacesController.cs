using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Auth;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AppRoles.ViewerPolicy)]
public class WorkspacesController : ControllerBase
{
    private readonly WorkspaceService _workspaceService;

    public WorkspacesController(WorkspaceService workspaceService)
    {
        _workspaceService = workspaceService;
    }

    /// <summary>Lists the workspaces on the configured allow-list.</summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkspaceInfo>>> Get(CancellationToken cancellationToken)
    {
        var workspaces = await _workspaceService.GetWorkspacesAsync(cancellationToken);
        return Ok(workspaces);
    }
}
