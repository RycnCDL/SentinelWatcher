namespace SentinelWatcher.Api.Models;

public class WorkspaceContext
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(SubscriptionId) &&
        !string.IsNullOrWhiteSpace(ResourceGroup) &&
        !string.IsNullOrWhiteSpace(WorkspaceName) &&
        !string.IsNullOrWhiteSpace(WorkspaceId);

    /// <summary>ARM resource ID for the Log Analytics workspace.</summary>
    public string ResourceId =>
        $"/subscriptions/{SubscriptionId}/resourceGroups/{ResourceGroup}/providers/Microsoft.OperationalInsights/workspaces/{WorkspaceName}";
}

public class WorkspaceInfo
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool SentinelEnabled { get; set; }
}

public class UserInfo
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}
