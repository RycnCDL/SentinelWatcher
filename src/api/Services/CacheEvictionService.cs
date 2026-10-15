namespace SentinelWatcher.Api.Services;

/// <summary>
/// Periodically removes cached workspace data that nobody has requested recently.
/// </summary>
public sealed class CacheEvictionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly WorkspaceCacheManager _cacheManager;
    private readonly ILogger<CacheEvictionService> _logger;

    public CacheEvictionService(WorkspaceCacheManager cacheManager, ILogger<CacheEvictionService> logger)
    {
        _cacheManager = cacheManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                _cacheManager.EvictStaleEntries(DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cache eviction failed");
            }
        }
    }
}
