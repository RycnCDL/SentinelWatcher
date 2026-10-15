namespace SentinelWatcher.Api.Models;

public class ConnectorHealth
{
    public string ConnectorId { get; set; } = string.Empty;
    public string ConnectorName { get; set; } = string.Empty;
    public string ConnectorType { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown"; // Healthy, Warning, Error, Quiet, Unknown
    public DateTime? LastLogReceived { get; set; }
    public double? GapMinutes { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> AssociatedTables { get; set; } = new();

    /// <summary>
    /// Associated tables in the data lake tier. Their last log time can't be read through the
    /// Log Analytics query API.
    /// </summary>
    public List<string> DataLakeTables { get; set; } = new();
}

public class DataConnectorsResponse
{
    public List<ConnectorHealth> Connectors { get; set; } = new();
    public int HealthyCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }

    /// <summary>Event-driven connectors without recent events (not an error).</summary>
    public int QuietCount { get; set; }
    public DateTime LastChecked { get; set; }
}
