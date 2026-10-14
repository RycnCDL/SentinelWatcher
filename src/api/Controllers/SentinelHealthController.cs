using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SentinelHealthController : ControllerBase
{
    private readonly LogAnalyticsService _logAnalyticsService;

    public SentinelHealthController(LogAnalyticsService logAnalyticsService)
    {
        _logAnalyticsService = logAnalyticsService;
    }

    [HttpGet]
    public async Task<ActionResult<SentinelHealthResponse>> Get(
        [FromQuery] string subscriptionId,
        [FromQuery] string resourceGroup,
        [FromQuery] string workspaceName,
        [FromQuery] string workspaceId,
        [FromQuery] int days = 7)
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

        var result = await _logAnalyticsService.GetSentinelHealthAsync(workspace, days);
        return Ok(result);
    }
}
