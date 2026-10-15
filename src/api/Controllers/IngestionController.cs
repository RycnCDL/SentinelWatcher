using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class IngestionController : WorkspaceControllerBase
{
    private readonly WorkspaceCacheManager _cacheManager;

    public IngestionController(WorkspaceRegistry registry, WorkspaceCacheManager cacheManager)
        : base(registry)
    {
        _cacheManager = cacheManager;
    }

    [HttpGet]
    public async Task<ActionResult<IngestionResponse>> Get([FromQuery] string? workspace)
    {
        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        await _cacheManager.EnsureDataAvailableAsync(ws, HttpContext.RequestAborted);

        var entry = _cacheManager.GetOrCreateEntry(ws);
        if (entry.Ingestion == null)
            return StatusCode(503, new { message = "Data is still loading. Please try again shortly." });

        return Ok(entry.Ingestion);
    }
}
