namespace SentinelWatcher.Api.Models;

/// <summary>
/// Bound from the "ConnectorHealth" configuration section: when a connector without new data
/// counts as Warning or Error. Gaps are measured from the newest record in the connector's tables.
/// Last log times are read for the last 24 hours, so thresholds above 1440 minutes have no effect.
/// </summary>
public class ConnectorHealthOptions
{
    public const string SectionName = "ConnectorHealth";

    /// <summary>Default gap in minutes after which a connector is Warning.</summary>
    public int WarningAfterMinutes { get; set; } = 60;

    /// <summary>Default gap in minutes after which a connector is Error.</summary>
    public int ErrorAfterMinutes { get; set; } = 120;

    /// <summary>
    /// Overrides per connector type (for example "AzureActivity"). They are merged with the
    /// built-in defaults in ConnectorThresholds; set only the values you want to change.
    /// </summary>
    public Dictionary<string, ConnectorThresholdOverride> Connectors { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public class ConnectorThresholdOverride
{
    public int? WarningAfterMinutes { get; set; }
    public int? ErrorAfterMinutes { get; set; }

    /// <summary>
    /// The connector only sends data when something happens (for example Azure Activity). A gap
    /// longer than the warning threshold makes it Quiet instead of Warning or Error.
    /// </summary>
    public bool? EventDriven { get; set; }
}

/// <summary>Resolved thresholds for one connector type.</summary>
public readonly record struct ConnectorThreshold(int WarningAfterMinutes, int ErrorAfterMinutes, bool EventDriven);
