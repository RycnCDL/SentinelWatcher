using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class WorkspaceService
{
    private readonly ResourceGraphService _resourceGraph;
    private readonly ILogger<WorkspaceService> _logger;

    public WorkspaceService(
        ResourceGraphService resourceGraph,
        ILogger<WorkspaceService> logger)
    {
        _resourceGraph = resourceGraph;
        _logger = logger;
    }

    /// <summary>
    /// Discovers all Log Analytics workspaces with Sentinel enabled that the user can access.
    /// Uses Azure Resource Graph for efficient cross-subscription discovery in a single round-trip.
    /// </summary>
    public async Task<List<WorkspaceInfo>> GetSentinelWorkspacesAsync()
    {
        _logger.LogInformation("Discovering Sentinel workspaces via Azure Resource Graph");
        return await _resourceGraph.GetSentinelWorkspacesAsync();
    }
}
