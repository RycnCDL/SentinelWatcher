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

    private readonly LogAnalyticsService _logAnalyticsService;

    public SentinelHealthController(WorkspaceRegistry registry, LogAnalyticsService logAnalyticsService)
        : base(registry)
    {
        _logAnalyticsService = logAnalyticsService;
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

        var result = await _logAnalyticsService.GetSentinelHealthAsync(ws, days);
        return Ok(result);
    }
}
