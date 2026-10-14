using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

public class WorkspaceRegistryTests
{
    private const string Valid =
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-sentinel" +
        "/providers/Microsoft.OperationalInsights/workspaces/law-allowed";

    private static WorkspaceRegistry Create(params string[] workspaces) =>
        new(Options.Create(new SentinelOptions { Workspaces = workspaces.ToList() }),
            NullLogger<WorkspaceRegistry>.Instance);

    [Fact]
    public void Parse_ExtractsSegmentsFromConfiguration()
    {
        var ws = WorkspaceRegistry.Parse(Valid);

        Assert.Equal("00000000-0000-0000-0000-000000000000", ws.SubscriptionId);
        Assert.Equal("rg-sentinel", ws.ResourceGroup);
        Assert.Equal("law-allowed", ws.WorkspaceName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-resource-id")]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/sa")]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.OperationalInsights/workspaces/law?x=1")]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg/providers/Microsoft.OperationalInsights/workspaces/law#frag")]
    public void InvalidConfiguration_FailsAtStartup(string workspace)
    {
        Assert.Throws<InvalidOperationException>(() => Create(workspace));
    }

    [Fact]
    public void TryResolve_MatchesCaseInsensitivelyAndReturnsConfiguredValue()
    {
        var registry = Create(Valid);

        var status = registry.TryResolve(Valid.ToUpperInvariant() + "/", out var ws);

        Assert.Equal(WorkspaceLookupStatus.Found, status);
        Assert.Equal(Valid, ws!.ResourceId);
    }

    [Theory]
    [InlineData(null, WorkspaceLookupStatus.Missing)]
    [InlineData("", WorkspaceLookupStatus.Missing)]
    [InlineData("   ", WorkspaceLookupStatus.Missing)]
    [InlineData("/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-other/providers/Microsoft.OperationalInsights/workspaces/law-allowed", WorkspaceLookupStatus.NotAllowed)]
    public void TryResolve_RejectsMissingAndUnlisted(string? requested, WorkspaceLookupStatus expected)
    {
        var registry = Create(Valid);

        Assert.Equal(expected, registry.TryResolve(requested, out var ws));
        Assert.Null(ws);
    }

    [Fact]
    public void DuplicateEntries_AreCollapsed()
    {
        var registry = Create(Valid, Valid.ToLowerInvariant());

        Assert.Single(registry.All);
    }
}
