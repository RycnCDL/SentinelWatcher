using Microsoft.Extensions.DependencyInjection;
using SentinelWatcher.Api.Services;

namespace SentinelWatcher.Api.Tests;

public class CacheEvictionTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public CacheEvictionTests(TestApp app)
    {
        _app = app;
    }

    private static string WorkspaceId(string name) =>
        "/subscriptions/00000000-0000-0000-0000-000000000000/resourceGroups/rg-cache" +
        $"/providers/Microsoft.OperationalInsights/workspaces/{name}";

    [Fact]
    public void EvictStaleEntries_RemovesOnlyEntriesNotAccessedWithinTtl()
    {
        var cache = _app.Services.GetRequiredService<WorkspaceCacheManager>();
        var stale = WorkspaceRegistry.Parse(WorkspaceId("law-stale"));
        var recent = WorkspaceRegistry.Parse(WorkspaceId("law-recent"));
        var now = DateTime.UtcNow;

        cache.GetOrCreateEntry(stale).LastAccessed = now.AddMinutes(-31);
        cache.GetOrCreateEntry(recent).LastAccessed = now.AddMinutes(-5);

        var evicted = cache.EvictStaleEntries(now);

        Assert.Contains(stale.Key, evicted);
        Assert.DoesNotContain(recent.Key, evicted);
    }

    [Fact]
    public void EvictStaleEntries_KeepsEntriesThatAreRefreshing()
    {
        var cache = _app.Services.GetRequiredService<WorkspaceCacheManager>();
        var refreshing = WorkspaceRegistry.Parse(WorkspaceId("law-refreshing"));
        var now = DateTime.UtcNow;

        var entry = cache.GetOrCreateEntry(refreshing);
        entry.LastAccessed = now.AddHours(-2);
        entry.IsRefreshing = true;

        var evicted = cache.EvictStaleEntries(now);

        Assert.DoesNotContain(refreshing.Key, evicted);
    }
}
