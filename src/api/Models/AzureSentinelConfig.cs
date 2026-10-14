namespace SentinelWatcher.Api.Models;

public class AzureSentinelConfig
{
    public string SubscriptionId { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public string WorkspaceId { get; set; } = string.Empty;
}

public class CacheSettingsConfig
{
    public int HeavyQueryRefreshMinutes { get; set; } = 15;
    public int ConnectorGapThresholdMinutes { get; set; } = 60;
    public double CostPerGBDollars { get; set; } = 2.76;
}
