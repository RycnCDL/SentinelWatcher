namespace SentinelWatcher.Api.Models;

/// <summary>
/// Bound from the "Sentinel" configuration section.
/// </summary>
public class SentinelOptions
{
    public const string SectionName = "Sentinel";

    /// <summary>
    /// Allow-list of Log Analytics workspace resource IDs, for example
    /// /subscriptions/{id}/resourceGroups/{rg}/providers/Microsoft.OperationalInsights/workspaces/{name}.
    /// This list is the only source of workspaces: requests for any other workspace are rejected.
    /// </summary>
    public List<string> Workspaces { get; set; } = new();
}
