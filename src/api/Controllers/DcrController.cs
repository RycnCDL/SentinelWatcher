using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class DcrController : WorkspaceControllerBase
{
    private readonly DataCollectionRuleService _dcrService;
    private readonly LogAnalyticsService _logAnalyticsService;
    private readonly TableTierService _tableTierService;

    public DcrController(
        WorkspaceRegistry registry,
        DataCollectionRuleService dcrService,
        LogAnalyticsService logAnalyticsService,
        TableTierService tableTierService)
        : base(registry)
    {
        _dcrService = dcrService;
        _logAnalyticsService = logAnalyticsService;
        _tableTierService = tableTierService;
    }

    [HttpGet]
    public async Task<ActionResult<DcrResponse>> Get([FromQuery] string? workspace)
    {
        var ws = ResolveWorkspace(workspace, out var error);
        if (ws is null) return error!;

        // Live query, not cached (table tiers are cached for 15 minutes)
        var lastLogTask = _logAnalyticsService.GetLastLogPerTableAsync(ws);
        var tiersTask = _tableTierService.GetTiersAsync(ws, HttpContext.RequestAborted);
        await Task.WhenAll(lastLogTask, tiersTask);
        var result = await _dcrService.GetDcrStatusAsync(ws, lastLogTask.Result, tiersTask.Result);
        return Ok(result);
    }
}
