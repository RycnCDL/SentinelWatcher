using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class WorkspaceCacheEntry
{
    public AnalyticsRulesResponse? AnalyticsRules { get; set; }
    public IngestionResponse? Ingestion { get; set; }
    public DataConnectorsResponse? Connectors { get; set; }
    public DateTime LastRefreshed { get; set; }
    public DateTime LastAccessed { get; set; }
    public bool IsRefreshing { get; set; }
    public SemaphoreSlim RefreshLock { get; } = new(1, 1);
}

public class WorkspaceCacheManager
{
    private readonly ConcurrentDictionary<string, WorkspaceCacheEntry> _caches = new();
    private readonly ILogger<WorkspaceCacheManager> _logger;
    private readonly int _refreshMinutes;
    private readonly int _evictMinutes;

    public WorkspaceCacheManager(
        IOptions<CacheSettingsConfig> cacheConfig,
        ILogger<WorkspaceCacheManager> logger)
    {
        _logger = logger;
        _refreshMinutes = cacheConfig.Value.HeavyQueryRefreshMinutes;
        _evictMinutes = 30;
    }

    public WorkspaceCacheEntry GetOrCreateEntry(string workspaceId)
    {
        var entry = _caches.GetOrAdd(workspaceId, _ => new WorkspaceCacheEntry());
        entry.LastAccessed = DateTime.UtcNow;
        return entry;
    }

    public bool IsCacheFresh(string workspaceId)
    {
        if (!_caches.TryGetValue(workspaceId, out var entry)) return false;
        return entry.AnalyticsRules != null &&
               entry.Ingestion != null &&
               entry.Connectors != null &&
               (DateTime.UtcNow - entry.LastRefreshed).TotalMinutes < _refreshMinutes;
    }

    public bool HasAnyData(string workspaceId)
    {
        if (!_caches.TryGetValue(workspaceId, out var entry)) return false;
        return entry.AnalyticsRules != null || entry.Ingestion != null || entry.Connectors != null;
    }

    /// <summary>
    /// Ensures data is available for the given workspace.
    /// - Fresh cache: returns immediately.
    /// - Stale cache with data: returns immediately, triggers background refresh.
    /// - No data at all: triggers refresh and waits up to 90 seconds (first load).
    /// </summary>
    public async Task EnsureDataAvailableAsync(
        string workspaceId,
        WorkspaceContext workspace,
        SentinelService sentinelService,
        LogAnalyticsService logAnalyticsService)
    {
        if (IsCacheFresh(workspaceId)) return;

        var entry = GetOrCreateEntry(workspaceId);

        if (HasAnyData(workspaceId))
        {
            // Stale but usable — return current data and refresh in background
            if (!entry.IsRefreshing)
                _ = Task.Run(() => RefreshCacheAsync(workspaceId, workspace, sentinelService, logAnalyticsService));
            return;
        }

        // First load — ensure refresh is running, then wait for data
        if (!entry.IsRefreshing)
            _ = Task.Run(() => RefreshCacheAsync(workspaceId, workspace, sentinelService, logAnalyticsService));

        // Poll until data arrives or timeout (90s)
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (!HasAnyData(workspaceId) && DateTime.UtcNow < deadline)
            await Task.Delay(500);
    }

    public async Task RefreshCacheAsync(
        string workspaceId,
        WorkspaceContext workspace,
        SentinelService sentinelService,
        LogAnalyticsService logAnalyticsService)
    {
        var entry = GetOrCreateEntry(workspaceId);

        if (!await entry.RefreshLock.WaitAsync(TimeSpan.FromSeconds(2)))
        {
            _logger.LogInformation("Cache refresh already in progress for workspace {Id}", workspaceId);
            return;
        }

        try
        {
            if (IsCacheFresh(workspaceId)) return;

            entry.IsRefreshing = true;
            _logger.LogInformation("Refreshing cache for workspace {Id}...", workspaceId);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Phase 1: independent queries in parallel
            // GetLastLogPerTableAsync now uses a single union query — much faster than before
            var rulesTask    = sentinelService.GetAnalyticsRulesAsync(workspace);
            var ingestionTask = logAnalyticsService.GetIngestionStatsAsync(workspace);
            var lastLogTask  = logAnalyticsService.GetLastLogPerTableAsync(workspace);

            await Task.WhenAll(rulesTask, ingestionTask, lastLogTask);

            // Store partial results immediately after Phase 1 to unblock the frontend.
            // Analytics: rule list from ARG (name, severity, enabled) with 0 incident counts.
            // This lets the rules table render right away; Phase 2 will overwrite with real counts.
            var partialRules = rulesTask.Result;
            entry.AnalyticsRules = new AnalyticsRulesResponse
            {
                TotalRules        = partialRules.Count,
                TotalIncidents    = 0,
                LastRefreshed     = DateTime.UtcNow,
                Top20MostIncidents = partialRules
                    .OrderBy(r => r.Name)
                    .Take(20)
                    .Select(r => new AnalyticsRuleStats
                    {
                        RuleId = r.Id, RuleName = r.Name,
                        Severity = r.Severity, Enabled = r.Enabled
                    }).ToList(),
                Top20LeastIncidents = new List<AnalyticsRuleStats>()
            };
            entry.Ingestion  = ingestionTask.Result;
            entry.Connectors = await sentinelService.GetDataConnectorsAsync(workspace, lastLogTask.Result);

            _logger.LogInformation("Phase 1 complete for workspace {Id} in {Ms}ms — data now available to frontend",
                workspaceId, sw.ElapsedMilliseconds);

            // Phase 2: SecurityIncident 30d query (overwrites partial analytics with real incident counts)
            var analyticsStats = await logAnalyticsService.GetAnalyticsRuleStatsAsync(workspace, rulesTask.Result);
            entry.AnalyticsRules = analyticsStats;
            entry.LastRefreshed  = DateTime.UtcNow;

            sw.Stop();
            _logger.LogInformation("Cache fully refreshed for workspace {Id} in {Ms}ms", workspaceId, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh cache for workspace {Id}", workspaceId);
        }
        finally
        {
            entry.IsRefreshing = false;
            entry.RefreshLock.Release();
        }
    }

    public void EvictStaleEntries()
    {
        foreach (var (key, entry) in _caches)
        {
            if ((DateTime.UtcNow - entry.LastAccessed).TotalMinutes > _evictMinutes)
            {
                _caches.TryRemove(key, out _);
                _logger.LogInformation("Evicted stale cache for workspace {Id}", key);
            }
        }
    }
}
