using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DcrController : ControllerBase
{
    private readonly DataCollectionRuleService _dcrService;
    private readonly LogAnalyticsService _logAnalyticsService;

    public DcrController(
        DataCollectionRuleService dcrService,
        LogAnalyticsService logAnalyticsService)
    {
        _dcrService = dcrService;
        _logAnalyticsService = logAnalyticsService;
    }

    [HttpGet]
    public async Task<ActionResult<DcrResponse>> Get(
        [FromQuery] string subscriptionId,
        [FromQuery] string resourceGroup,
        [FromQuery] string workspaceName,
        [FromQuery] string workspaceId)
    {
        var workspace = new WorkspaceContext
        {
            SubscriptionId = subscriptionId,
            ResourceGroup = resourceGroup,
            WorkspaceName = workspaceName,
            WorkspaceId = workspaceId
        };

        if (!workspace.IsValid)
            return BadRequest(new { message = "Missing workspace parameters" });

        // Live query - not cached
        var lastLogPerTable = await _logAnalyticsService.GetLastLogPerTableAsync(workspace);
        var result = await _dcrService.GetDcrStatusAsync(workspace, lastLogPerTable);
        return Ok(result);
    }
}
