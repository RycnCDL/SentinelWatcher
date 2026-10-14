using Azure.Core;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public enum WorkspaceLookupStatus
{
    Found,
    Missing,
    Invalid,
    NotAllowed,
}

/// <summary>
/// The configured workspace allow-list (Sentinel:Workspaces). It is the single source of
/// truth for which workspaces the app may read. Invalid entries fail at startup.
/// </summary>
public sealed class WorkspaceRegistry
{
    public const int MaxResourceIdLength = 512;

    private const string WorkspaceResourceType = "Microsoft.OperationalInsights/workspaces";
    private static readonly char[] ForbiddenChars = { '?', '#', '%', '\\', ' ' };

    private readonly Dictionary<string, WorkspaceContext> _byResourceId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<WorkspaceContext> All { get; }

    public WorkspaceRegistry(IOptions<SentinelOptions> options, ILogger<WorkspaceRegistry> logger)
    {
        var workspaces = new List<WorkspaceContext>();
        foreach (var entry in options.Value.Workspaces)
        {
            var workspace = Parse(entry);
            if (_byResourceId.TryAdd(workspace.ResourceId, workspace))
                workspaces.Add(workspace);
        }
        All = workspaces;

        if (workspaces.Count == 0)
        {
            logger.LogWarning(
                "No workspaces configured. Set Sentinel:Workspaces to one or more Log Analytics workspace resource IDs.");
        }
    }

    /// <summary>Parses and validates one configured workspace resource ID.</summary>
    public static WorkspaceContext Parse(string? resourceId)
    {
        var value = Normalize(resourceId);

        if (value.Length == 0 || value.Length > MaxResourceIdLength ||
            value.IndexOfAny(ForbiddenChars) >= 0 || value.Any(char.IsControl) ||
            !ResourceIdentifier.TryParse(value, out var id) || id is null)
        {
            throw new InvalidOperationException(
                $"Sentinel:Workspaces contains an invalid resource ID: '{resourceId}'.");
        }

        if (!string.Equals(id.ResourceType.ToString(), WorkspaceResourceType, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(id.SubscriptionId) || string.IsNullOrEmpty(id.ResourceGroupName))
        {
            throw new InvalidOperationException(
                $"Sentinel:Workspaces entry '{resourceId}' is not a Log Analytics workspace resource ID " +
                $"(/subscriptions/{{id}}/resourceGroups/{{rg}}/providers/{WorkspaceResourceType}/{{name}}).");
        }

        return new WorkspaceContext
        {
            ResourceId = value,
            SubscriptionId = id.SubscriptionId,
            ResourceGroup = id.ResourceGroupName,
            WorkspaceName = id.Name,
        };
    }

    /// <summary>
    /// Looks up a workspace requested by a client. Only exact (case-insensitive) matches
    /// against the allow-list succeed; the returned context comes from configuration.
    /// </summary>
    public WorkspaceLookupStatus TryResolve(string? requested, out WorkspaceContext? workspace)
    {
        workspace = null;

        if (string.IsNullOrWhiteSpace(requested))
            return WorkspaceLookupStatus.Missing;

        if (requested.Length > MaxResourceIdLength)
            return WorkspaceLookupStatus.Invalid;

        return _byResourceId.TryGetValue(Normalize(requested), out workspace)
            ? WorkspaceLookupStatus.Found
            : WorkspaceLookupStatus.NotAllowed;
    }

    private static string Normalize(string? resourceId) => (resourceId ?? string.Empty).Trim().TrimEnd('/');
}
