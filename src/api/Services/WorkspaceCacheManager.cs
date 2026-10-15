using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

/// <summary>
/// Cached data of one workspace. Each section has its data (the last successful result, if any)
/// and the error of the latest refresh, if it failed. A failure never replaces data with empty
/// results: the API returns the error, or the older data marked with RefreshError.
/// </summary>
public class WorkspaceCacheEntry
{
    public AnalyticsRulesResponse? AnalyticsRules { get; set; }
    public LoadError? AnalyticsRulesError { get; set; }
    public IngestionResponse? Ingestion { get; set; }
    public LoadError? IngestionError { get; set; }
    public DataConnectorsResponse? Connectors { get; set; }
    public LoadError? ConnectorsError { get; set; }
    public DcrResponse? Dcr { get; set; }
    public LoadError? DcrError { get; set; }

    /// <summary>When the latest refresh finished (successfully or not).</summary>
    public DateTime LastRefreshed { get; set; }
    public DateTime LastAccessed { get; set; }

    /// <summary>The running or last refresh. Only one refresh per workspace runs at a time.</summary>
    public Task? RefreshTask { get; internal set; }

    public bool IsRefreshing => RefreshTask is { IsCompleted: false };

    /// <summary>True when any section of the latest refresh failed; such data is retried soon.</summary>
    public bool HasErrors =>
        AnalyticsRulesError != null || IngestionError != null || ConnectorsError != null || DcrError != null ||
        AnalyticsRules?.IncidentsError != null;

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
    private readonly TimeSpan _retryAfter;
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
        _retryAfter = TimeSpan.FromSeconds(Math.Max(0, cacheConfig.Value.ErrorRetrySeconds));
        _evictMinutes = cacheConfig.Value.EvictAfterMinutes;
        _dcrEnabled = sentinelOptions.Value.EnableDcrPage;
    }

    public WorkspaceCacheEntry GetOrCreateEntry(WorkspaceContext workspace)
    {
        var entry = _caches.GetOrAdd(workspace.Key, _ => new WorkspaceCacheEntry());
        entry.LastAccessed = DateTime.UtcNow;
        return entry;
    }

    // Data with errors is retried after ErrorRetrySeconds instead of the full refresh interval.
    private bool IsFresh(WorkspaceCacheEntry entry) =>
        entry.LastRefreshed != default &&
        DateTime.UtcNow - entry.LastRefreshed < (entry.HasErrors ? _retryAfter : _refreshAfter);

    // Data or an error for at least one section: enough to answer a request.
    private static bool HasAnyResult(WorkspaceCacheEntry entry) =>
        entry.AnalyticsRules != null || entry.Ingestion != null || entry.Connectors != null || entry.Dcr != null ||
        entry.AnalyticsRulesError != null || entry.IngestionError != null || entry.ConnectorsError != null || entry.DcrError != null;

    /// <summary>
    /// Ensures data (or an error) is available for the given workspace.
    /// - Fresh cache: returns immediately.
    /// - Stale cache: returns immediately, the refresh runs in the background.
    /// - Nothing yet: starts (or joins) the refresh and waits up to 90 seconds for the first results.
    /// </summary>
    public async Task EnsureDataAvailableAsync(WorkspaceContext workspace, CancellationToken cancellationToken = default)
    {
        var entry = GetOrCreateEntry(workspace);
        if (IsFresh(entry)) return;

        var refresh = StartRefresh(workspace, entry, force: false);
        if (HasAnyResult(entry)) return;

        var deadline = DateTime.UtcNow + FirstLoadWait;
        while (!HasAnyResult(entry) && !refresh.IsCompleted && DateTime.UtcNow < deadline)
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

            // Phase 1: independent reads in parallel. Each section succeeds or fails on its own.
            var rulesTask     = CaptureAsync("analytics rules", workspace, () => _sentinelService.GetAnalyticsRulesAsync(workspace));
            var ingestionTask = CaptureAsync("ingestion", workspace, () => _logAnalyticsService.GetIngestionStatsAsync(workspace));
            var lastLogTask   = CaptureAsync("last log per table", workspace, () => _logAnalyticsService.GetLastLogPerTableAsync(workspace));
            var tiersTask     = _tableTierService.GetTiersAsync(workspace); // degrades to "unknown" on errors
            await Task.WhenAll(rulesTask, ingestionTask, lastLogTask, tiersTask);

            var rules   = rulesTask.Result;
            var lastLog = lastLogTask.Result;
            var tiers   = tiersTask.Result;

            if (ingestionTask.Result.Value is { } ingestion)
            {
                foreach (var table in ingestion.Tables)
                    table.Tier = TableTiers.Of(tiers, table.TableName);
            }
            (entry.Ingestion, entry.IngestionError) = Merge(entry.Ingestion, ingestionTask.Result);

            // Connectors and DCRs need the last log times; without them every table would look silent.
            var connectorsTask = lastLog.Value is { } lastLogValue
                ? CaptureAsync("data connectors", workspace, () => _sentinelService.GetDataConnectorsAsync(workspace, lastLogValue, tiers))
                : Task.FromResult(new SectionResult<DataConnectorsResponse>(null, lastLog.Error));
            var dcrTask = !_dcrEnabled
                ? Task.FromResult(new SectionResult<DcrResponse>(null, null))
                : lastLog.Value is { } lastLogForDcr
                    ? CaptureAsync("data collection rules", workspace, () => _dcrService.GetDcrStatusAsync(workspace, lastLogForDcr, tiers))
                    : Task.FromResult(new SectionResult<DcrResponse>(null, lastLog.Error));
            await Task.WhenAll(connectorsTask, dcrTask);

            (entry.Connectors, entry.ConnectorsError) = Merge(entry.Connectors, connectorsTask.Result);
            if (_dcrEnabled)
                (entry.Dcr, entry.DcrError) = Merge(entry.Dcr, dcrTask.Result);

            if (rules.Value is null)
            {
                entry.AnalyticsRulesError = rules.Error;
                if (entry.AnalyticsRules != null) entry.AnalyticsRules.RefreshError = rules.Error;
            }
            else
            {
                entry.AnalyticsRulesError = null;
                // Rule list without incident counts, so the rules table can render before Phase 2.
                entry.AnalyticsRules ??= IncidentAttribution.Build(
                    rules.Value, Array.Empty<IncidentGroup>(), totalIncidents: 0, incidentsLoaded: false);
            }

            _logger.LogInformation("Phase 1 complete for workspace {Workspace} in {Ms} ms",
                workspace.WorkspaceName, sw.ElapsedMilliseconds);

            // Phase 2: incidents per rule (SecurityIncident, 30 days).
            if (rules.Value is { } ruleList)
            {
                var incidents = await CaptureAsync("incidents", workspace,
                    () => _logAnalyticsService.GetAnalyticsRuleStatsAsync(workspace, ruleList));

                if (incidents.Value is { } stats)
                {
                    entry.AnalyticsRules = stats;
                }
                else if (entry.AnalyticsRules is { IncidentsLoaded: true, IncidentsError: null } previous)
                {
                    previous.RefreshError = incidents.Error; // keep the older counts, marked as such
                }
                else
                {
                    var withoutCounts = IncidentAttribution.Build(
                        ruleList, Array.Empty<IncidentGroup>(), totalIncidents: 0, incidentsLoaded: false);
                    withoutCounts.IncidentsError = incidents.Error;
                    entry.AnalyticsRules = withoutCounts;
                }
            }

            _logger.LogInformation("Cache refreshed for workspace {Workspace} in {Ms} ms{Errors}",
                workspace.WorkspaceName, sw.ElapsedMilliseconds, entry.HasErrors ? " with errors (retrying soon)" : "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh cache for workspace {Workspace}", workspace.WorkspaceName);
        }
        finally
        {
            entry.LastRefreshed = DateTime.UtcNow;
        }
    }

    private readonly record struct SectionResult<T>(T? Value, LoadError? Error) where T : class;

    private async Task<SectionResult<T>> CaptureAsync<T>(string section, WorkspaceContext workspace, Func<Task<T>> load)
        where T : class
    {
        try
        {
            return new SectionResult<T>(await load(), null);
        }
        catch (Exception ex)
        {
            var error = LoadError.From(ex);
            _logger.LogWarning("Section {Section} failed for workspace {Workspace}: {Code} ({ExceptionType}: {ExceptionMessage})",
                section, workspace.WorkspaceName, error.Code, ex.GetType().Name, ex.Message);
            return new SectionResult<T>(null, error);
        }
    }

    // A success replaces the data; a failure keeps the older data (marked) and records the error.
    private static (T? Data, LoadError? Error) Merge<T>(T? current, SectionResult<T> result) where T : class
    {
        if (result.Value is not null) return (result.Value, null);
        if (current is not null && result.Error is not null) SetRefreshError(current, result.Error);
        return (current, result.Error);
    }

    private static void SetRefreshError(object data, LoadError error)
    {
        switch (data)
        {
            case IngestionResponse i: i.RefreshError = error; break;
            case DataConnectorsResponse c: c.RefreshError = error; break;
            case DcrResponse d: d.RefreshError = error; break;
            case AnalyticsRulesResponse a: a.RefreshError = error; break;
        }
    }

    /// <summary>
    /// Sentinel Health for a trend window, cached like the other data. Concurrent requests for the
    /// same workspace and window share one query. A failed query is retried after ErrorRetrySeconds.
    /// </summary>
    public Task<SentinelHealthResponse> GetSentinelHealthAsync(WorkspaceContext workspace, int days)
    {
        var entry = GetOrCreateEntry(workspace);
        lock (entry.Sync)
        {
            if (entry.Health.TryGetValue(days, out var cached))
            {
                var age = DateTime.UtcNow - cached.Requested;
                if (!cached.Task.IsCompleted ||
                    (cached.Task.IsCompletedSuccessfully && age < _refreshAfter) ||
                    (!cached.Task.IsCompletedSuccessfully && age < _retryAfter))
                    return cached.Task;
            }

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
