using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class DataConnectorsController : WorkspaceControllerBase
{
    private readonly WorkspaceCacheManager _cacheManager;

    public DataConnectorsController(WorkspaceRegistry registry, WorkspaceCacheManager cacheManager)
        : base(registry)
    {
        _cacheManager = cacheManager;
    }

    [HttpGet]
    public async Task<ActionResult<DataConnectorsResponse>> Get([FromQuery] string? workspace)
    {
        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        await _cacheManager.EnsureDataAvailableAsync(ws, HttpContext.RequestAborted);

        var entry = _cacheManager.GetOrCreateEntry(ws);
        if (entry.Connectors == null)
            return StatusCode(503, new { message = "Data is still loading. Please try again shortly." });

        return Ok(entry.Connectors);
    }
}
