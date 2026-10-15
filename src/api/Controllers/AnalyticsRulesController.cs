using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class AnalyticsRulesController : WorkspaceControllerBase
{
    private readonly WorkspaceCacheManager _cacheManager;

    public AnalyticsRulesController(WorkspaceRegistry registry, WorkspaceCacheManager cacheManager)
        : base(registry)
    {
        _cacheManager = cacheManager;
    }

    [HttpGet]
    public async Task<ActionResult<AnalyticsRulesResponse>> Get([FromQuery] string? workspace)
    {
        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        await _cacheManager.EnsureDataAvailableAsync(ws, HttpContext.RequestAborted);

        var entry = _cacheManager.GetOrCreateEntry(ws);
        return Section(entry.AnalyticsRules, entry.AnalyticsRulesError);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromQuery] string? workspace)
    {
        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        await _cacheManager.RefreshAsync(ws);
        return Ok(new { message = "Cache refreshed" });
    }
}
