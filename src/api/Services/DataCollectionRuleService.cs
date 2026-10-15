using System.Text.Json;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class DataCollectionRuleService
{
    private const string ApiVersion = "2023-03-11";

    private readonly ArmRestClient _arm;
    private readonly ILogger<DataCollectionRuleService> _logger;

    public DataCollectionRuleService(
        ArmRestClient arm,
        ILogger<DataCollectionRuleService> logger)
    {
        _arm = arm;
        _logger = logger;
    }

    /// <summary>
    /// Lists the data collection rules in the workspace resource group. DCRs in other
    /// resource groups are not included (known limitation).
    /// </summary>
    public async Task<DcrResponse> GetDcrStatusAsync(
        WorkspaceContext workspace,
        Dictionary<string, DateTime?> lastLogPerTable,
        IReadOnlyDictionary<string, string> tableTiers)
    {
        var response = new DcrResponse { LastChecked = DateTime.UtcNow };
        var now = response.LastChecked;

        try
        {
            var dcrItems = await _arm.GetAllPagesAsync(
                $"/subscriptions/{workspace.SubscriptionId}/resourceGroups/{workspace.ResourceGroup}" +
                "/providers/Microsoft.Insights/dataCollectionRules",
                ApiVersion);

            foreach (var dcr in dcrItems)
            {
                var dcrStatus = new DcrStatus
                {
                    DcrId     = dcr.TryGetProperty("id",       out var idP)   ? idP.GetString()   ?? ""        : "",
                    DcrName   = dcr.TryGetProperty("name",     out var nameP) ? nameP.GetString() ?? "Unknown" : "Unknown",
                    Location  = dcr.TryGetProperty("location", out var locP)  ? locP.GetString()  ?? ""        : ""
                };

                if (dcr.TryGetProperty("properties", out var props))
                {
                    if (props.TryGetProperty("dataSources", out var dataSources))
                        ExtractDataSources(dataSources, dcrStatus);

                    dcrStatus.Destinations = BuildDestinations(props, workspace.ResourceId, lastLogPerTable, tableTiers, now);
                }

                DetermineStatus(dcrStatus);
                LogTablesWithoutDataOnce(workspace, dcrStatus);
                response.DataCollectionRules.Add(dcrStatus);
            }

            response.TotalCount   = response.DataCollectionRules.Count;
            response.HealthyCount = response.DataCollectionRules.Count(d => d.Status == "Healthy");
            response.WarningCount = response.DataCollectionRules.Count(d => d.Status == "Warning");
            response.ErrorCount   = response.DataCollectionRules.Count(d => d.Status == "Error");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to get DCR status");
        }

        return response;
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _loggedEmptyTables = new();

    /// <summary>
    /// Logs once per DCR and table when a destination table in this workspace had no data in the
    /// last 24 hours, so an operator can see which table makes a DCR show Error.
    /// </summary>
    private void LogTablesWithoutDataOnce(WorkspaceContext workspace, DcrStatus dcr)
    {
        foreach (var d in dcr.Destinations.Where(d =>
                     d.Destination == ThisWorkspace && d.Tier != TableTiers.DataLake && !d.IsReceivingData))
        {
            if (_loggedEmptyTables.TryAdd($"{workspace.Key}|{dcr.DcrId}|{d.TableName}", 0))
                _logger.LogInformation("DCR {Dcr}: destination table {Table} has no data in the last 24 hours",
                    dcr.DcrName, d.TableName);
        }
    }

    // ── Data source extraction ────────────────────────────────────────────────

    private static void ExtractDataSources(JsonElement dataSources, DcrStatus dcrStatus)
    {
        if (dataSources.TryGetProperty("windowsEventLogs", out var winLogs))
            foreach (var src in winLogs.EnumerateArray())
                if (src.TryGetProperty("name", out var n)) dcrStatus.DataSources.Add($"WindowsEventLog: {n.GetString()}");

        if (dataSources.TryGetProperty("syslog", out var syslogs))
            foreach (var src in syslogs.EnumerateArray())
                if (src.TryGetProperty("name", out var n)) dcrStatus.DataSources.Add($"Syslog: {n.GetString()}");

        if (dataSources.TryGetProperty("performanceCounters", out var perf))
            foreach (var src in perf.EnumerateArray())
                if (src.TryGetProperty("name", out var n)) dcrStatus.DataSources.Add($"PerfCounter: {n.GetString()}");

        if (dataSources.TryGetProperty("extensions", out var ext))
            foreach (var src in ext.EnumerateArray())
                if (src.TryGetProperty("name", out var n)) dcrStatus.DataSources.Add($"Extension: {n.GetString()}");
    }

    // ── Destination + data flow extraction ───────────────────────────────────

    public const string ThisWorkspace = "ThisWorkspace";
    public const string OtherWorkspace = "OtherWorkspace";

    /// <summary>
    /// Builds the Log Analytics destinations of a DCR from its properties. Each data flow writes
    /// to its outputStream when set, otherwise to its input streams. Freshness can only be checked
    /// for tables in the selected workspace; destinations in other workspaces are only labeled.
    /// </summary>
    public static List<DcrDestination> BuildDestinations(
        JsonElement properties,
        string workspaceResourceId,
        IReadOnlyDictionary<string, DateTime?> lastLogPerTable,
        IReadOnlyDictionary<string, string> tableTiers,
        DateTime utcNow)
    {
        var result = new List<DcrDestination>();
        if (!properties.TryGetProperty("destinations", out var destinations) ||
            !destinations.TryGetProperty("logAnalytics", out var logAnalytics) ||
            !properties.TryGetProperty("dataFlows", out var dataFlows))
            return result;

        foreach (var dest in logAnalytics.EnumerateArray())
        {
            var destName = dest.TryGetProperty("name", out var dn) ? dn.GetString() ?? "" : "";
            var destWorkspace = dest.TryGetProperty("workspaceResourceId", out var wId) ? wId.GetString() ?? "" : "";
            var isThisWorkspace = string.Equals(
                destWorkspace.Trim().TrimEnd('/'), workspaceResourceId, StringComparison.OrdinalIgnoreCase);

            var tables = new List<string>();
            foreach (var flow in dataFlows.EnumerateArray())
            {
                if (!FlowTargetsDest(flow, destName)) continue;

                if (flow.TryGetProperty("outputStream", out var outputStream) &&
                    !string.IsNullOrEmpty(outputStream.GetString()))
                {
                    tables.Add(MapStreamToTable(outputStream.GetString()!));
                }
                else if (flow.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                        tables.Add(MapStreamToTable(stream.GetString() ?? ""));
                }
            }

            foreach (var table in tables.Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var destination = new DcrDestination
                {
                    TableName   = table,
                    Destination = isThisWorkspace ? ThisWorkspace : OtherWorkspace,
                    Tier        = isThisWorkspace ? TableTiers.Of(tableTiers, table) : TableTiers.Unknown
                };

                if (isThisWorkspace && lastLogPerTable.TryGetValue(table, out var lastLog) && lastLog.HasValue)
                {
                    destination.LastLogReceived = lastLog;
                    destination.IsReceivingData = (utcNow - lastLog.Value).TotalHours < 24;
                }

                result.Add(destination);
            }
        }

        return result;
    }

    private static bool FlowTargetsDest(JsonElement flow, string destName)
    {
        if (!flow.TryGetProperty("destinations", out var dests)) return false;
        foreach (var d in dests.EnumerateArray())
            if (d.GetString() == destName) return true;
        return false;
    }

    // ── Status determination ──────────────────────────────────────────────────

    /// <summary>
    /// Status from the destinations whose tables can be queried: tables in the selected workspace
    /// that are not in the data lake tier. Other destinations are left out.
    /// </summary>
    public static void DetermineStatus(DcrStatus dcrStatus)
    {
        var inWorkspace = dcrStatus.Destinations.Where(d => d.Destination == ThisWorkspace).ToList();
        var checkable = inWorkspace.Where(d => d.Tier != TableTiers.DataLake).ToList();

        if (dcrStatus.Destinations.Count == 0)
        {
            dcrStatus.Status       = "Unknown";
            dcrStatus.ErrorMessage = "No destinations configured or detected";
        }
        else if (inWorkspace.Count == 0)
        {
            dcrStatus.Status       = "Unknown";
            dcrStatus.ErrorMessage = "All destinations are in another workspace, so data flow can't be checked from this workspace";
        }
        else if (checkable.Count == 0)
        {
            dcrStatus.Status       = "Unknown";
            dcrStatus.ErrorMessage = "Not available in data lake tier: all destination tables are in the data lake tier, " +
                                     "so data flow can't be checked through the Log Analytics query API";
        }
        else if (checkable.All(d => d.IsReceivingData))
        {
            dcrStatus.Status           = "Healthy";
            dcrStatus.LastDataReceived = checkable
                .Where(d => d.LastLogReceived.HasValue)
                .Max(d => d.LastLogReceived);
        }
        else if (checkable.Any(d => d.IsReceivingData))
        {
            dcrStatus.Status           = "Warning";
            dcrStatus.ErrorMessage     = "Some destinations not receiving data";
            dcrStatus.LastDataReceived = checkable
                .Where(d => d.LastLogReceived.HasValue)
                .Max(d => d.LastLogReceived);
        }
        else
        {
            dcrStatus.Status       = "Error";
            dcrStatus.ErrorMessage = "No destinations receiving data in the last 24 hours";
        }
    }

    // ── Stream-to-table mapping ───────────────────────────────────────────────

    /// <summary>
    /// Maps a DCR stream to its destination table: "Microsoft-Table-SecurityEvent" and
    /// "Microsoft-SecurityEvent" to SecurityEvent, "Custom-MyTable_CL" to MyTable_CL.
    /// </summary>
    public static string MapStreamToTable(string streamName)
    {
        foreach (var prefix in new[] { "Microsoft-Table-", "Custom-", "Microsoft-" })
        {
            if (streamName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return streamName[prefix.Length..];
        }
        return streamName;
    }
}
