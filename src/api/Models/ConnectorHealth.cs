namespace SentinelWatcher.Api.Models;

public class ConnectorHealth
{
    public string ConnectorId { get; set; } = string.Empty;
    public string ConnectorName { get; set; } = string.Empty;
    public string ConnectorType { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown"; // Healthy, Warning, Error, Unknown
    public DateTime? LastLogReceived { get; set; }
    public double? GapMinutes { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> AssociatedTables { get; set; } = new();
}

public class DataConnectorsResponse
{
    public List<ConnectorHealth> Connectors { get; set; } = new();
    public int HealthyCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime LastChecked { get; set; }
}
