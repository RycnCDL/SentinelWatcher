using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[Route("api/[controller]")]
public class DcrController : WorkspaceControllerBase
{
    private readonly DataCollectionRuleService _dcrService;
    private readonly LogAnalyticsService _logAnalyticsService;
    private readonly TableTierService _tableTierService;
    private readonly bool _enabled;

    public DcrController(
        WorkspaceRegistry registry,
        DataCollectionRuleService dcrService,
        LogAnalyticsService logAnalyticsService,
        TableTierService tableTierService,
        IOptions<SentinelOptions> options)
        : base(registry)
    {
        _dcrService = dcrService;
        _logAnalyticsService = logAnalyticsService;
        _tableTierService = tableTierService;
        _enabled = options.Value.EnableDcrPage;
    }

    [HttpGet]
    public async Task<ActionResult<DcrResponse>> Get([FromQuery] string? workspace)
    {
        // Disabled by configuration (Sentinel:EnableDcrPage): no DCR is read.
        if (!_enabled)
            return NotFound(new { message = "The Data Collection Rules page is disabled." });

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
