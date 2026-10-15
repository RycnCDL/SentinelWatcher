using System.Collections.Concurrent;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public interface IWorkspaceStatusProvider
{
    /// <summary>True or false, or null when the status could not be read.</summary>
    Task<bool?> IsSentinelEnabledAsync(WorkspaceContext workspace, CancellationToken cancellationToken);
}

/// <summary>
/// Detects whether Microsoft Sentinel is enabled on a workspace.
/// Primary check: the SecurityInsights(workspace) solution resource in the workspace resource group.
/// Fallback: the Microsoft.SecurityInsights onboarding state, for workspaces onboarded without
/// the legacy solution resource.
/// </summary>
public class SentinelStatusProvider : IWorkspaceStatusProvider
{
    private const string SolutionApiVersion = "2015-11-01-preview";
    private const string OnboardingApiVersion = "2024-03-01";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly ArmRestClient _arm;
    private readonly ILogger<SentinelStatusProvider> _logger;
    private readonly ConcurrentDictionary<string, (bool? Enabled, DateTimeOffset CheckedAt)> _cache = new();

    public SentinelStatusProvider(ArmRestClient arm, ILogger<SentinelStatusProvider> logger)
    {
        _arm = arm;
        _logger = logger;
    }

    public async Task<bool?> IsSentinelEnabledAsync(WorkspaceContext workspace, CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(workspace.Key, out var cached) &&
            DateTimeOffset.UtcNow - cached.CheckedAt < (cached.Enabled.HasValue ? CacheDuration : RetryAfter))
            return cached.Enabled;

        var enabled = await CheckAsync(workspace, cancellationToken);
        _cache[workspace.Key] = (enabled, DateTimeOffset.UtcNow);
        return enabled;
    }

    /// <summary>
    /// True when the solution or the onboarding state exists, false when both return 404, and
    /// null when a check failed, so a failure is never reported as "Sentinel not enabled".
    /// </summary>
    private async Task<bool?> CheckAsync(WorkspaceContext workspace, CancellationToken cancellationToken)
    {
        var solutionPath =
            $"/subscriptions/{workspace.SubscriptionId}/resourceGroups/{workspace.ResourceGroup}" +
            $"/providers/Microsoft.OperationsManagement/solutions/SecurityInsights({workspace.WorkspaceName})";

        bool? solutionExists = null;
        try
        {
            solutionExists = await _arm.ExistsAsync(solutionPath, SolutionApiVersion, cancellationToken);
            if (solutionExists == true) return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the SecurityInsights solution for workspace {Workspace}", workspace.WorkspaceName);
        }

        try
        {
            var onboardingPath = $"{workspace.ResourceId}/providers/Microsoft.SecurityInsights/onboardingStates/default";
            if (await _arm.ExistsAsync(onboardingPath, OnboardingApiVersion, cancellationToken))
                return true;
            return solutionExists == false ? false : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the Sentinel onboarding state for workspace {Workspace}", workspace.WorkspaceName);
            return null;
        }
    }
}
