namespace SentinelWatcher.Api.Models;

public class TableIngestion
{
    public string TableName { get; set; } = string.Empty;
    public double VolumeMB { get; set; }
    public double VolumeGB => Math.Round(VolumeMB / 1024, 2);
    public double EstimatedCostUSD { get; set; }
    public double DailyAverageMB { get; set; }

    /// <summary>
    /// True when the table has no Usage data and the volume is estimated from its record
    /// count (1 KB per record) instead of measured.
    /// </summary>
    public bool IsEstimated { get; set; }
}

public class IngestionResponse
{
    public List<TableIngestion> Tables { get; set; } = new();
    public double TotalVolumeMB { get; set; }
    public double TotalVolumeGB => Math.Round(TotalVolumeMB / 1024, 2);
    public double TotalEstimatedCostUSD { get; set; }
    public int DaysAnalyzed { get; set; }
    public DateTime LastRefreshed { get; set; }
}
