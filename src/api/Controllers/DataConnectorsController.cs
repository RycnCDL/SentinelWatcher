using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DataConnectorsController : ControllerBase
{
    private readonly WorkspaceCacheManager _cacheManager;
    private readonly SentinelService _sentinelService;
    private readonly LogAnalyticsService _logAnalyticsService;

    public DataConnectorsController(
        WorkspaceCacheManager cacheManager,
        SentinelService sentinelService,
        LogAnalyticsService logAnalyticsService)
    {
        _cacheManager = cacheManager;
        _sentinelService = sentinelService;
        _logAnalyticsService = logAnalyticsService;
    }

    [HttpGet]
    public async Task<ActionResult<DataConnectorsResponse>> Get(
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

        await _cacheManager.EnsureDataAvailableAsync(workspaceId, workspace, _sentinelService, _logAnalyticsService);

        var entry = _cacheManager.GetOrCreateEntry(workspaceId);
        if (entry.Connectors == null)
            return StatusCode(503, new { message = "Data is still loading. Please try again shortly." });

        return Ok(entry.Connectors);
    }
}
