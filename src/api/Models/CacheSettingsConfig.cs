namespace SentinelWatcher.Api.Models;

public class CacheSettingsConfig
{
    public int HeavyQueryRefreshMinutes { get; set; } = 15;
    public int ConnectorGapThresholdMinutes { get; set; } = 60;
    public double CostPerGBDollars { get; set; } = 2.76;
}
