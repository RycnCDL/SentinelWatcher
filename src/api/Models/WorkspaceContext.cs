using Azure.Core;

namespace SentinelWatcher.Api.Models;

/// <summary>
/// A workspace from the configured allow-list. Every value comes from configuration,
/// never from the request, so it is safe to use when building ARM URLs.
/// </summary>
public sealed class WorkspaceContext
{
    public required string ResourceId { get; init; }
    public required string SubscriptionId { get; init; }
    public required string ResourceGroup { get; init; }
    public required string WorkspaceName { get; init; }

    public ResourceIdentifier ResourceIdentifier => new(ResourceId);

    /// <summary>Case-insensitive key for lookups and the cache.</summary>
    public string Key => ResourceId.ToLowerInvariant();
}

public class WorkspaceInfo
{
    public string ResourceId { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    /// <summary>Null when the status could not be read.</summary>
    public bool? SentinelEnabled { get; set; }
}
