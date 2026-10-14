using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public interface IWorkspaceStatusProvider
{
    Task<bool> IsSentinelEnabledAsync(WorkspaceContext workspace, CancellationToken cancellationToken);
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

    private readonly ArmRestClient _arm;
    private readonly ILogger<SentinelStatusProvider> _logger;

    public SentinelStatusProvider(ArmRestClient arm, ILogger<SentinelStatusProvider> logger)
    {
        _arm = arm;
        _logger = logger;
    }

    public async Task<bool> IsSentinelEnabledAsync(WorkspaceContext workspace, CancellationToken cancellationToken)
    {
        var solutionPath =
            $"/subscriptions/{workspace.SubscriptionId}/resourceGroups/{workspace.ResourceGroup}" +
            $"/providers/Microsoft.OperationsManagement/solutions/SecurityInsights({workspace.WorkspaceName})";

        try
        {
            if (await _arm.ExistsAsync(solutionPath, SolutionApiVersion, cancellationToken))
                return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the SecurityInsights solution for workspace {Workspace}", workspace.WorkspaceName);
        }

        try
        {
            var onboardingPath = $"{workspace.ResourceId}/providers/Microsoft.SecurityInsights/onboardingStates/default";
            return await _arm.ExistsAsync(onboardingPath, OnboardingApiVersion, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the Sentinel onboarding state for workspace {Workspace}", workspace.WorkspaceName);
            return false;
        }
    }
}
