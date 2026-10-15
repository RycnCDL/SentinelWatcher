using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class SentinelHealthController : WorkspaceControllerBase
{
    public const int MinDays = 1;
    public const int MaxDays = 30;

    private readonly WorkspaceCacheManager _cacheManager;

    public SentinelHealthController(WorkspaceRegistry registry, WorkspaceCacheManager cacheManager)
        : base(registry)
    {
        _cacheManager = cacheManager;
    }

    /// <param name="workspace">Workspace resource ID from the allow-list.</param>
    /// <param name="days">Trend window in days (1 to 30). Out-of-range values return 400.</param>
    [HttpGet]
    public async Task<ActionResult<SentinelHealthResponse>> Get(
        [FromQuery] string? workspace,
        [FromQuery, Range(MinDays, MaxDays)] int days = 7)
    {
        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        try
        {
            return Ok(await _cacheManager.GetSentinelHealthAsync(ws, days));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never "health monitoring not enabled" for a failure; the cache retries soon.
            return SectionError(LoadError.From(ex));
        }
    }
}
