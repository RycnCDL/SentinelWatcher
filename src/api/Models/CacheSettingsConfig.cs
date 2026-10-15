namespace SentinelWatcher.Api.Models;

/// <summary>
/// Bound from the "CacheSettings" configuration section.
/// </summary>
public class CacheSettingsConfig
{
    /// <summary>
    /// Time to live of cached workspace data in minutes. Older data is still served while a
    /// background refresh runs.
    /// </summary>
    public int HeavyQueryRefreshMinutes { get; set; } = 10;

    /// <summary>
    /// Cached data for a workspace that nobody requested for this many minutes is removed.
    /// </summary>
    public int EvictAfterMinutes { get; set; } = 30;

    public double CostPerGBDollars { get; set; } = 2.76;
}
