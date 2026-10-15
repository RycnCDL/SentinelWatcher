using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class WorkspaceService
{
    private readonly WorkspaceRegistry _registry;
    private readonly IWorkspaceStatusProvider _statusProvider;

    public WorkspaceService(WorkspaceRegistry registry, IWorkspaceStatusProvider statusProvider)
    {
        _registry = registry;
        _statusProvider = statusProvider;
    }

    /// <summary>Returns the configured workspaces with their Sentinel status.</summary>
    public async Task<List<WorkspaceInfo>> GetWorkspacesAsync(CancellationToken cancellationToken)
    {
        var tasks = _registry.All.Select(async ws => new WorkspaceInfo
        {
            ResourceId = ws.ResourceId,
            WorkspaceName = ws.WorkspaceName,
            SubscriptionId = ws.SubscriptionId,
            ResourceGroup = ws.ResourceGroup,
            SentinelEnabled = await _statusProvider.IsSentinelEnabledAsync(ws, cancellationToken),
        });

        return (await Task.WhenAll(tasks)).ToList();
    }
}
