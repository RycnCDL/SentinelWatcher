using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class DcrController : WorkspaceControllerBase
{
    private readonly DataCollectionRuleService _dcrService;
    private readonly LogAnalyticsService _logAnalyticsService;

    public DcrController(
        WorkspaceRegistry registry,
        DataCollectionRuleService dcrService,
        LogAnalyticsService logAnalyticsService)
        : base(registry)
    {
        _dcrService = dcrService;
        _logAnalyticsService = logAnalyticsService;
    }

    [HttpGet]
    public async Task<ActionResult<DcrResponse>> Get([FromQuery] string? workspace)
    {
        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        // Live query - not cached
        var lastLogPerTable = await _logAnalyticsService.GetLastLogPerTableAsync(ws);
        var result = await _dcrService.GetDcrStatusAsync(ws, lastLogPerTable);
        return Ok(result);
    }
}
