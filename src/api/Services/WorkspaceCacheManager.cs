using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class WorkspaceCacheEntry
{
    public AnalyticsRulesResponse? AnalyticsRules { get; set; }
    public IngestionResponse? Ingestion { get; set; }
    public DataConnectorsResponse? Connectors { get; set; }
    public DcrResponse? Dcr { get; set; }
    public DateTime LastRefreshed { get; set; }
    public DateTime LastAccessed { get; set; }

    /// <summary>The running or last refresh. Only one refresh per workspace runs at a time.</summary>
    public Task? RefreshTask { get; internal set; }

    public bool IsRefreshing => RefreshTask is { IsCompleted: false };

    internal object Sync { get; } = new();

    /// <summary>Sentinel Health results per trend window (days), with the time they were requested.</summary>
    internal Dictionary<int, (Task<SentinelHealthResponse> Task, DateTime Requested)> Health { get; } = new();
}

/// <summary>
/// Single entry point for workspace data. Every endpoint reads through this cache, so queries that
/// several pages need (last log per table, table tiers) run once per refresh, not once per request.
/// </summary>
public class WorkspaceCacheManager
{
    private static readonly TimeSpan FirstLoadWait = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<string, WorkspaceCacheEntry> _caches = new();
    private readonly SentinelService _sentinelService;
    private readonly LogAnalyticsService _logAnalyticsService;
    private readonly TableTierService _tableTierService;
    private readonly DataCollectionRuleService _dcrService;
    private readonly ILogger<WorkspaceCacheManager> _logger;
    private readonly TimeSpan _refreshAfter;
    private readonly int _evictMinutes;
    private readonly bool _dcrEnabled;

    // The services are singletons that call Azure with the app's own identity, so refreshes can
    // run in the background after the request that triggered them has completed.
    public WorkspaceCacheManager(
        SentinelService sentinelService,
        LogAnalyticsService logAnalyticsService,
        TableTierService tableTierService,
        DataCollectionRuleService dcrService,
        IOptions<CacheSettingsConfig> cacheConfig,
        IOptions<SentinelOptions> sentinelOptions,
        ILogger<WorkspaceCacheManager> logger)
    {
        _sentinelService = sentinelService;
        _logAnalyticsService = logAnalyticsService;
        _tableTierService = tableTierService;
        _dcrService = dcrService;
        _logger = logger;
        _refreshAfter = TimeSpan.FromMinutes(cacheConfig.Value.HeavyQueryRefreshMinutes);
        _evictMinutes = cacheConfig.Value.EvictAfterMinutes;
        _dcrEnabled = sentinelOptions.Value.EnableDcrPage;
    }

    public WorkspaceCacheEntry GetOrCreateEntry(WorkspaceContext workspace)
    {
        var entry = _caches.GetOrAdd(workspace.Key, _ => new WorkspaceCacheEntry());
        entry.LastAccessed = DateTime.UtcNow;
        return entry;
    }

    private bool IsFresh(WorkspaceCacheEntry entry) =>
        entry.AnalyticsRules != null &&
        entry.Ingestion != null &&
        entry.Connectors != null &&
        (!_dcrEnabled || entry.Dcr != null) &&
        DateTime.UtcNow - entry.LastRefreshed < _refreshAfter;

    private static bool HasAnyData(WorkspaceCacheEntry entry) =>
        entry.AnalyticsRules != null || entry.Ingestion != null || entry.Connectors != null || entry.Dcr != null;

    /// <summary>
    /// Ensures data is available for the given workspace.
    /// - Fresh cache: returns immediately.
    /// - Stale cache with data: returns immediately, the refresh runs in the background.
    /// - No data at all: starts (or joins) the refresh and waits up to 90 seconds for the first data.
    /// </summary>
    public async Task EnsureDataAvailableAsync(WorkspaceContext workspace, CancellationToken cancellationToken = default)
    {
        var entry = GetOrCreateEntry(workspace);
        if (IsFresh(entry)) return;

        var refresh = StartRefresh(workspace, entry, force: false);
        if (HasAnyData(entry)) return;

        var deadline = DateTime.UtcNow + FirstLoadWait;
        while (!HasAnyData(entry) && !refresh.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(250, cancellationToken);
    }

    /// <summary>Refreshes now, or joins the refresh that is already running.</summary>
    public Task RefreshAsync(WorkspaceContext workspace)
    {
        var entry = GetOrCreateEntry(workspace);
        lock (entry.Sync)
        {
            entry.Health.Clear();
        }
        return StartRefresh(workspace, entry, force: true);
    }

    private Task StartRefresh(WorkspaceContext workspace, WorkspaceCacheEntry entry, bool force)
    {
        lock (entry.Sync)
        {
            if (entry.RefreshTask is { IsCompleted: false } running) return running;
            if (!force && IsFresh(entry)) return Task.CompletedTask;

            entry.RefreshTask = Task.Run(() => RefreshCoreAsync(workspace, entry));
            return entry.RefreshTask;
        }
    }

    private async Task RefreshCoreAsync(WorkspaceContext workspace, WorkspaceCacheEntry entry)
    {
        try
        {
            _logger.LogInformation("Refreshing cache for workspace {Workspace}", workspace.WorkspaceName);
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Phase 1: independent reads in parallel.
            var rulesTask     = _sentinelService.GetAnalyticsRulesAsync(workspace);
            var ingestionTask = _logAnalyticsService.GetIngestionStatsAsync(workspace);
            var lastLogTask   = _logAnalyticsService.GetLastLogPerTableAsync(workspace);
            var tiersTask     = _tableTierService.GetTiersAsync(workspace);
            await Task.WhenAll(rulesTask, ingestionTask, lastLogTask, tiersTask);

            var rules   = rulesTask.Result;
            var lastLog = lastLogTask.Result;
            var tiers   = tiersTask.Result;

            var ingestion = ingestionTask.Result;
            foreach (var table in ingestion.Tables)
                table.Tier = TableTiers.Of(tiers, table.TableName);

            // Connectors and DCRs both use the last log times and tiers read above.
            var connectorsTask = _sentinelService.GetDataConnectorsAsync(workspace, lastLog, tiers);
            var dcrTask = _dcrEnabled
                ? _dcrService.GetDcrStatusAsync(workspace, lastLog, tiers)
                : Task.FromResult<DcrResponse?>(null)!;
            await Task.WhenAll(connectorsTask, dcrTask);

            // Rule list without incident counts, so the rules table can render before Phase 2.
            entry.AnalyticsRules ??= IncidentAttribution.Build(
                rules, Array.Empty<IncidentGroup>(), totalIncidents: 0, incidentsLoaded: false);
            entry.Ingestion  = ingestion;
            entry.Connectors = connectorsTask.Result;
            entry.Dcr        = dcrTask.Result;

            _logger.LogInformation("Phase 1 complete for workspace {Workspace} in {Ms} ms",
                workspace.WorkspaceName, sw.ElapsedMilliseconds);

            // Phase 2: incidents per rule (SecurityIncident, 30 days).
            entry.AnalyticsRules = await _logAnalyticsService.GetAnalyticsRuleStatsAsync(workspace, rules);
            entry.LastRefreshed  = DateTime.UtcNow;

            _logger.LogInformation("Cache refreshed for workspace {Workspace} in {Ms} ms",
                workspace.WorkspaceName, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh cache for workspace {Workspace}", workspace.WorkspaceName);
        }
    }

    /// <summary>
    /// Sentinel Health for a trend window, cached like the other data. Concurrent requests for the
    /// same workspace and window share one query.
    /// </summary>
    public Task<SentinelHealthResponse> GetSentinelHealthAsync(WorkspaceContext workspace, int days)
    {
        var entry = GetOrCreateEntry(workspace);
        lock (entry.Sync)
        {
            if (entry.Health.TryGetValue(days, out var cached) &&
                (!cached.Task.IsCompleted ||
                 (cached.Task.IsCompletedSuccessfully && DateTime.UtcNow - cached.Requested < _refreshAfter)))
                return cached.Task;

            var task = _logAnalyticsService.GetSentinelHealthAsync(workspace, days);
            entry.Health[days] = (task, DateTime.UtcNow);
            return task;
        }
    }

    /// <summary>
    /// Removes entries that were not requested for EvictAfterMinutes. Entries that are being
    /// refreshed are kept. Called periodically by CacheEvictionService.
    /// </summary>
    /// <returns>The keys of the evicted entries.</returns>
    public IReadOnlyList<string> EvictStaleEntries(DateTime utcNow)
    {
        var evicted = new List<string>();
        foreach (var (key, entry) in _caches)
        {
            if (!entry.IsRefreshing &&
                (utcNow - entry.LastAccessed).TotalMinutes > _evictMinutes &&
                _caches.TryRemove(key, out _))
            {
                evicted.Add(key);
                _logger.LogInformation("Evicted stale cache for workspace {Id}", key);
            }
        }
        return evicted;
    }
}
