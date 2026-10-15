namespace SentinelWatcher.Api.Models;

public class DcrStatus
{
    public string DcrId { get; set; } = string.Empty;
    public string DcrName { get; set; } = string.Empty;
    public string Status { get; set; } = "Unknown"; // Healthy, Warning, Error, Unknown
    public string Location { get; set; } = string.Empty;
    public List<string> DataSources { get; set; } = new();
    public List<DcrDestination> Destinations { get; set; } = new();
    public DateTime? LastDataReceived { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DcrDestination
{
    public string TableName { get; set; } = string.Empty;

    /// <summary>
    /// ThisWorkspace or OtherWorkspace. Resource IDs of other workspaces are not sent to the
    /// browser, and their tables can't be checked from the selected workspace.
    /// </summary>
    public string Destination { get; set; } = "ThisWorkspace";

    public DateTime? LastLogReceived { get; set; }
    public bool IsReceivingData { get; set; }

    /// <summary>Tier of the destination table: Analytics, Basic, DataLake or Unknown.</summary>
    public string Tier { get; set; } = "Unknown";
}

public class DcrResponse
{
    public List<DcrStatus> DataCollectionRules { get; set; } = new();
    public int TotalCount { get; set; }
    public int HealthyCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public DateTime LastChecked { get; set; }
}
