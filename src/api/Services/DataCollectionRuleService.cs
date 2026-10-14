using System.Text.Json;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class DataCollectionRuleService
{
    private readonly ResourceGraphService _resourceGraph;
    private readonly ILogger<DataCollectionRuleService> _logger;

    public DataCollectionRuleService(
        ResourceGraphService resourceGraph,
        ILogger<DataCollectionRuleService> logger)
    {
        _resourceGraph = resourceGraph;
        _logger = logger;
    }

    public async Task<DcrResponse> GetDcrStatusAsync(WorkspaceContext workspace, Dictionary<string, DateTime?> lastLogPerTable)
    {
        var response = new DcrResponse { LastChecked = DateTime.UtcNow };

        try
        {
            var dcrItems = await _resourceGraph.GetDcrConfigAsync(workspace);

            foreach (var dcr in dcrItems)
            {
                var dcrStatus = new DcrStatus
                {
                    DcrId     = dcr.TryGetProperty("id",       out var idP)   ? idP.GetString()   ?? ""        : "",
                    DcrName   = dcr.TryGetProperty("name",     out var nameP) ? nameP.GetString() ?? "Unknown" : "Unknown",
                    Location  = dcr.TryGetProperty("location", out var locP)  ? locP.GetString()  ?? ""        : ""
                };

                if (dcr.TryGetProperty("dataSources", out var dataSources))
                    ExtractDataSources(dataSources, dcrStatus);

                if (dcr.TryGetProperty("destinations", out var destinations) &&
                    dcr.TryGetProperty("dataFlows",    out var dataFlows))
                    ExtractDestinations(destinations, dataFlows, dcrStatus, lastLogPerTable);

                DetermineStatus(dcrStatus);
                response.DataCollectionRules.Add(dcrStatus);
            }

            response.TotalCount   = response.DataCollectionRules.Count;
            response.HealthyCount = response.DataCollectionRules.Count(d => d.Status == "Healthy");
            response.WarningCount = response.DataCollectionRules.Count(d => d.Status == "Warning");
            response.ErrorCount   = response.DataCollectionRules.Count(d => d.Status == "Error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get DCR status");
        }

        return response;
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

    private static void ExtractDestinations(
        JsonElement destinations,
        JsonElement dataFlows,
        DcrStatus dcrStatus,
        Dictionary<string, DateTime?> lastLogPerTable)
    {
        if (!destinations.TryGetProperty("logAnalytics", out var logAnalytics))
            return;

        foreach (var dest in logAnalytics.EnumerateArray())
        {
            var destName    = dest.TryGetProperty("name",                out var dn)  ? dn.GetString()  ?? "" : "";
            var workspaceId = dest.TryGetProperty("workspaceResourceId", out var wId) ? wId.GetString() ?? "" : "";

            var tables = new List<string>();

            foreach (var flow in dataFlows.EnumerateArray())
            {
                if (!FlowTargetsDest(flow, destName)) continue;

                if (flow.TryGetProperty("streams", out var streams))
                    foreach (var stream in streams.EnumerateArray())
                    {
                        var tableName = MapStreamToTable(stream.GetString() ?? "");
                        if (!string.IsNullOrEmpty(tableName))
                            tables.Add(tableName);
                    }

                if (flow.TryGetProperty("outputStream", out var outputStream))
                {
                    var os = outputStream.GetString();
                    if (!string.IsNullOrEmpty(os)) tables.Add(os);
                }
            }

            foreach (var table in tables.Distinct().Where(t => !string.IsNullOrEmpty(t)))
            {
                var destination = new DcrDestination
                {
                    TableName   = table,
                    WorkspaceId = workspaceId
                };

                if (lastLogPerTable.TryGetValue(table, out var lastLog))
                {
                    destination.LastLogReceived = lastLog;
                    destination.IsReceivingData = lastLog.HasValue &&
                        (DateTime.UtcNow - lastLog.Value).TotalHours < 24;
                }

                dcrStatus.Destinations.Add(destination);
            }
        }
    }

    private static bool FlowTargetsDest(JsonElement flow, string destName)
    {
        if (!flow.TryGetProperty("destinations", out var dests)) return false;
        foreach (var d in dests.EnumerateArray())
            if (d.GetString() == destName) return true;
        return false;
    }

    // ── Status determination ──────────────────────────────────────────────────

    private static void DetermineStatus(DcrStatus dcrStatus)
    {
        if (dcrStatus.Destinations.Count == 0)
        {
            dcrStatus.Status       = "Unknown";
            dcrStatus.ErrorMessage = "No destinations configured or detected";
        }
        else if (dcrStatus.Destinations.All(d => d.IsReceivingData))
        {
            dcrStatus.Status           = "Healthy";
            dcrStatus.LastDataReceived = dcrStatus.Destinations
                .Where(d => d.LastLogReceived.HasValue)
                .Max(d => d.LastLogReceived);
        }
        else if (dcrStatus.Destinations.Any(d => d.IsReceivingData))
        {
            dcrStatus.Status           = "Warning";
            dcrStatus.ErrorMessage     = "Some destinations not receiving data";
            dcrStatus.LastDataReceived = dcrStatus.Destinations
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

    private static string MapStreamToTable(string streamName) => streamName switch
    {
        "Microsoft-WindowsEvent"      => "WindowsEvent",
        "Microsoft-SecurityEvent"     => "SecurityEvent",
        "Microsoft-Syslog"            => "Syslog",
        "Microsoft-CommonSecurityLog" => "CommonSecurityLog",
        "Microsoft-Perf"              => "Perf",
        "Microsoft-Event"             => "Event",
        "Microsoft-Heartbeat"         => "Heartbeat",
        "Microsoft-InsightsMetrics"   => "InsightsMetrics",
        _ when streamName.StartsWith("Custom-") =>
            streamName.Replace("Custom-", "").Replace("_CL", "") + "_CL",
        _ => streamName
    };
}
