using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class LogAnalyticsService
{
    private readonly UserAzureClientFactory _clientFactory;
    private readonly CacheSettingsConfig _cacheConfig;
    private readonly ILogger<LogAnalyticsService> _logger;

    public LogAnalyticsService(
        UserAzureClientFactory clientFactory,
        IOptions<CacheSettingsConfig> cacheConfig,
        ILogger<LogAnalyticsService> logger)
    {
        _clientFactory = clientFactory;
        _cacheConfig = cacheConfig.Value;
        _logger = logger;
    }

    public async Task<AnalyticsRulesResponse> GetAnalyticsRuleStatsAsync(
        WorkspaceContext workspace,
        List<(string Id, string Name, string Severity, bool Enabled)> rules)
    {
        var logsClient = await _clientFactory.CreateLogsQueryClientAsync();
        var response = new AnalyticsRulesResponse
        {
            LastRefreshed = DateTime.UtcNow,
            TotalRules = rules.Count
        };

        try
        {
            var kql = @"
                SecurityIncident
                | where TimeGenerated > ago(30d)
                | extend RuleIds = RelatedAnalyticRuleIds
                | mv-expand RuleId = RuleIds
                | summarize
                    IncidentCount = count(),
                    LastIncident = max(TimeGenerated)
                    by tostring(RuleId)
            ";

            var result = await logsClient.QueryWorkspaceAsync(
                workspace.WorkspaceId, kql, QueryTimeRange.All);

            var incidentMap = new Dictionary<string, (int Count, DateTime? Last)>();
            if (result.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in result.Value.Table.Rows)
                {
                    var ruleId = row.GetString("RuleId") ?? "";
                    var guidPart = ruleId.Contains("/")
                        ? ruleId.Split('/').Last()
                        : ruleId;
                    var count = row.GetInt32("IncidentCount") ?? 0;
                    var last = row.GetDateTimeOffset("LastIncident")?.UtcDateTime;
                    incidentMap[guidPart.ToLower()] = (count, last);
                }
            }

            response.TotalIncidents = incidentMap.Values.Sum(x => x.Count);

            var allStats = rules.Select(r =>
            {
                var ruleGuid = r.Id.Contains("/") ? r.Id.Split('/').Last() : r.Id;
                incidentMap.TryGetValue(ruleGuid.ToLower(), out var incidents);
                return new AnalyticsRuleStats
                {
                    RuleId = r.Id,
                    RuleName = r.Name,
                    Severity = r.Severity,
                    Enabled = r.Enabled,
                    IncidentCount = incidents.Count,
                    LastIncident = incidents.Last
                };
            }).ToList();

            response.Top20MostIncidents = allStats
                .OrderByDescending(r => r.IncidentCount)
                .Take(20)
                .ToList();

            response.Top20LeastIncidents = allStats
                .Where(r => r.Enabled)
                .OrderBy(r => r.IncidentCount)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query analytics rule stats");
        }

        return response;
    }

    public async Task<IngestionResponse> GetIngestionStatsAsync(WorkspaceContext workspace)
    {
        var logsClient = await _clientFactory.CreateLogsQueryClientAsync();
        var response = new IngestionResponse
        {
            DaysAnalyzed = 30,
            LastRefreshed = DateTime.UtcNow
        };

        try
        {
            // Primary: Usage table (fast, pre-aggregated by Azure)
            var kql = @"
                Usage
                | where TimeGenerated > ago(30d)
                | summarize TotalVolumeMB = sum(Quantity) by DataType
                | sort by TotalVolumeMB desc
            ";

            var result = await logsClient.QueryWorkspaceAsync(
                workspace.WorkspaceId, kql, QueryTimeRange.All);

            if (result.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in result.Value.Table.Rows)
                {
                    var tableName = row.GetString("DataType") ?? "Unknown";
                    var volumeMB = row.GetDouble("TotalVolumeMB") ?? 0;
                    var volumeGB = volumeMB / 1024;
                    var cost = volumeGB * _cacheConfig.CostPerGBDollars;

                    response.Tables.Add(new TableIngestion
                    {
                        TableName = tableName,
                        VolumeMB = Math.Round(volumeMB, 2),
                        EstimatedCostUSD = Math.Round(cost, 2),
                        DailyAverageMB = Math.Round(volumeMB / 30, 2)
                    });
                }
            }

            // Check important tables not found in Usage — single union query instead of N individual queries
            var foundTables      = new HashSet<string>(response.Tables.Select(t => t.TableName));
            var importantMissing = ImportantTables.Where(t => !foundTables.Contains(t)).ToArray();

            if (importantMissing.Length > 0)
            {
                _logger.LogInformation("Checking {Count} tables not in Usage via union query...", importantMissing.Length);
                try
                {
                    var tableList   = string.Join(", ", importantMissing);
                    var missingKql  = $@"
                        union isfuzzy=true {tableList}
                        | where TimeGenerated > ago(30d)
                        | summarize RecordCount = count() by Type
                        | where RecordCount > 0
                    ";

                    var missingResult = await logsClient.QueryWorkspaceAsync(
                        workspace.WorkspaceId, missingKql, QueryTimeRange.All);

                    var missingStatus = missingResult.Value.Status;
                    if (missingStatus == LogsQueryResultStatus.Success || missingStatus == LogsQueryResultStatus.PartialFailure)
                    {
                        foreach (var row in missingResult.Value.Table.Rows)
                        {
                            var tableName   = row.GetString("Type") ?? "";
                            var recordCount = row.GetInt64("RecordCount") ?? 0;
                            if (string.IsNullOrEmpty(tableName) || recordCount == 0) continue;

                            var estimatedMB = Math.Round(recordCount * 1.0 / 1024, 2);
                            var estimatedGB = estimatedMB / 1024;
                            var cost        = estimatedGB * _cacheConfig.CostPerGBDollars;

                            response.Tables.Add(new TableIngestion
                            {
                                TableName        = tableName,
                                VolumeMB         = estimatedMB,
                                EstimatedCostUSD = Math.Round(cost, 2),
                                DailyAverageMB   = Math.Round(estimatedMB / 30, 2)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Union ingestion query failed for missing tables, skipping");
                }
            }

            response.Tables = response.Tables.OrderByDescending(t => t.VolumeMB).ToList();
            response.TotalVolumeMB = response.Tables.Sum(t => t.VolumeMB);
            response.TotalEstimatedCostUSD = response.Tables.Sum(t => t.EstimatedCostUSD);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query ingestion stats");
        }

        return response;
    }

    // Important Sentinel/Defender tables that Usage table might miss
    private static readonly string[] ImportantTables = new[]
    {
        "DeviceEvents", "DeviceLogonEvents", "DeviceProcessEvents",
        "DeviceNetworkEvents", "DeviceFileEvents", "DeviceRegistryEvents",
        "DeviceImageLoadEvents", "DeviceInfo", "AlertEvidence", "AlertInfo",
        "EmailEvents", "EmailAttachmentInfo", "EmailUrlInfo", "EmailPostDeliveryEvents",
        "IdentityLogonEvents", "IdentityQueryEvents", "IdentityDirectoryEvents",
        "CloudAppEvents", "UrlClickEvents",
        "SigninLogs", "AuditLogs",
        "AADNonInteractiveUserSignInLogs", "AADServicePrincipalSignInLogs",
        "AADManagedIdentitySignInLogs", "AADProvisioningLogs",
        "SecurityAlert", "SecurityIncident",
        "OfficeActivity", "AzureActivity",
        "CommonSecurityLog", "Syslog",
        "ThreatIntelligenceIndicator",
        "SecurityEvent", "WindowsEvent",
        "McasShadowItReporting",
        "SentinelHealth", "SentinelAudit",
        "InsiderRiskEvents"
    };

    public async Task<Dictionary<string, DateTime?>> GetLastLogPerTableAsync(WorkspaceContext workspace)
    {
        var logsClient = await _clientFactory.CreateLogsQueryClientAsync();
        var result = new Dictionary<string, DateTime?>();

        _logger.LogInformation("Querying last log times for {Count} tables via single union query...", ImportantTables.Length);

        try
        {
            // Single union query with isfuzzy=true — silently skips tables that don't exist.
            // The built-in Type column contains the source table name in Log Analytics.
            // This replaces 36 individual queries with 1, which is significantly faster.
            var tableList = string.Join(", ", ImportantTables);
            var kql = $@"
                union isfuzzy=true {tableList}
                | where TimeGenerated > ago(24h)
                | summarize LastLog = max(TimeGenerated) by Type
                | where isnotnull(LastLog)
            ";

            var queryResult = await logsClient.QueryWorkspaceAsync(
                workspace.WorkspaceId, kql, QueryTimeRange.All);

            // Accept both Success and PartialFailure — isfuzzy=true raises PartialFailure
            // when some tables don't exist in the workspace, but still returns valid rows.
            var status = queryResult.Value.Status;
            if (status == LogsQueryResultStatus.Success || status == LogsQueryResultStatus.PartialFailure)
            {
                foreach (var row in queryResult.Value.Table.Rows)
                {
                    var tableName = row.GetString("Type") ?? "";
                    var lastLog   = row.GetDateTimeOffset("LastLog")?.UtcDateTime;
                    if (!string.IsNullOrEmpty(tableName) && lastLog.HasValue)
                        result[tableName] = lastLog;
                }
            }

            _logger.LogInformation("Found {Count} tables with recent data (status: {Status})",
                result.Count, status);

            // If union returned nothing at all, fall back to individual queries
            if (result.Count == 0)
            {
                _logger.LogWarning("Union returned 0 results, falling back to parallel individual queries");
                result = await GetLastLogPerTableParallelAsync(workspace, logsClient);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Union query threw exception, falling back to parallel individual queries");
            result = await GetLastLogPerTableParallelAsync(workspace, logsClient);
        }

        return result;
    }

    private async Task<Dictionary<string, DateTime?>> GetLastLogPerTableParallelAsync(
        WorkspaceContext workspace, LogsQueryClient logsClient)
    {
        var result    = new Dictionary<string, DateTime?>();
        var semaphore = new SemaphoreSlim(10);

        var tasks = ImportantTables.Select(async table =>
        {
            await semaphore.WaitAsync();
            try
            {
                var kql = $"{table} | where TimeGenerated > ago(24h) | summarize LastLog = max(TimeGenerated)";
                var qr  = await logsClient.QueryWorkspaceAsync(workspace.WorkspaceId, kql, QueryTimeRange.All);

                if (qr.Value.Status == LogsQueryResultStatus.Success && qr.Value.Table.Rows.Count > 0)
                {
                    var lastLog = qr.Value.Table.Rows[0].GetDateTimeOffset("LastLog")?.UtcDateTime;
                    if (lastLog.HasValue)
                        lock (result) { result[table] = lastLog; }
                }
            }
            catch { /* Table doesn't exist */ }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);
        _logger.LogInformation("Parallel fallback found {Count} tables", result.Count);
        return result;
    }

    public async Task<SentinelHealthResponse> GetSentinelHealthAsync(WorkspaceContext workspace, int days = 7)
    {
        var logsClient = await _clientFactory.CreateLogsQueryClientAsync();
        var response = new SentinelHealthResponse { QueryTime = DateTime.UtcNow };

        // Quick probe: does the workspace have any SentinelHealth data?
        try
        {
            var probeKql = "_SentinelHealth() | take 1 | project TimeGenerated";
            var probe = await logsClient.QueryWorkspaceAsync(workspace.WorkspaceId, probeKql, QueryTimeRange.All);
            if (probe.Value.Status != LogsQueryResultStatus.Success || probe.Value.Table.Rows.Count == 0)
            {
                response.HealthMonitoringEnabled = false;
                return response;
            }
            response.HealthMonitoringEnabled = true;
        }
        catch
        {
            response.HealthMonitoringEnabled = false;
            return response;
        }

        // Run all 5 queries in parallel
        var summaryTask       = QuerySummaryAsync(logsClient, workspace);
        var trendTask         = QueryTrendAsync(logsClient, workspace, days);
        var connectorsTask    = QueryConnectorStatusAsync(logsClient, workspace);
        var autoDisabledTask  = QueryAutoDisabledRulesAsync(logsClient, workspace);
        var recentFailTask    = QueryRecentFailuresAsync(logsClient, workspace);

        await Task.WhenAll(summaryTask, trendTask, connectorsTask, autoDisabledTask, recentFailTask);

        // Populate response from results
        var summary = summaryTask.Result;
        response.TotalConnectors    = summary.GetValueOrDefault("DataConnector_Total");
        response.HealthyConnectors  = summary.GetValueOrDefault("DataConnector_Success");
        response.TotalRuleRuns      = summary.GetValueOrDefault("AnalyticsRule_Total");
        response.FailedRuleRuns     = summary.GetValueOrDefault("AnalyticsRule_Failure");
        response.TotalAutomationRuns  = summary.GetValueOrDefault("AutomationRule_Total") + summary.GetValueOrDefault("Playbook_Total");
        response.FailedAutomationRuns = summary.GetValueOrDefault("AutomationRule_Failure") + summary.GetValueOrDefault("Playbook_Failure");

        response.Trend               = trendTask.Result;
        response.ConnectorStatuses   = connectorsTask.Result;
        response.AutoDisabledRules   = autoDisabledTask.Result;
        response.AutoDisabledRulesCount = response.AutoDisabledRules.Count;
        response.RecentFailures      = recentFailTask.Result;

        // Health Score: % of resource runs that succeeded in last 24h
        var totalRuns   = summary.Values.Where((v, i) => i % 2 == 0).Sum(); // all *_Total
        var failedRuns  = response.FailedRuleRuns + response.FailedAutomationRuns +
                          (response.TotalConnectors - response.HealthyConnectors);
        response.HealthScore = totalRuns > 0
            ? (int)Math.Round(100.0 * (totalRuns - failedRuns) / totalRuns)
            : 100;
        response.HealthScore = Math.Clamp(response.HealthScore, 0, 100);

        return response;
    }

    // ── Helper queries ────────────────────────────────────────────────────────

    private async Task<Dictionary<string, int>> QuerySummaryAsync(LogsQueryClient client, WorkspaceContext ws)
    {
        var result = new Dictionary<string, int>();
        try
        {
            var kql = @"
                _SentinelHealth()
                | where TimeGenerated > ago(24h)
                | summarize
                    Total   = count(),
                    Success = countif(Status == 'Success'),
                    Failure = countif(Status != 'Success')
                    by SentinelResourceType
            ";
            var qr = await client.QueryWorkspaceAsync(ws.WorkspaceId, kql, QueryTimeRange.All);
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    var type = (row.GetString("SentinelResourceType") ?? "").Replace(" ", "");
                    result[$"{type}_Total"]   = (int)(row.GetInt64("Total") ?? 0);
                    result[$"{type}_Success"] = (int)(row.GetInt64("Success") ?? 0);
                    result[$"{type}_Failure"] = (int)(row.GetInt64("Failure") ?? 0);
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SentinelHealth summary query failed"); }
        return result;
    }

    private async Task<List<HealthTrendPoint>> QueryTrendAsync(LogsQueryClient client, WorkspaceContext ws, int days)
    {
        var points = new Dictionary<string, HealthTrendPoint>();
        try
        {
            var kql = $@"
                _SentinelHealth()
                | where TimeGenerated > ago({days}d)
                | where Status != 'Success'
                | summarize Failures = count() by SentinelResourceType, Day = bin(TimeGenerated, 1d)
                | order by Day asc
            ";
            var qr = await client.QueryWorkspaceAsync(ws.WorkspaceId, kql, QueryTimeRange.All);
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    var day = row.GetDateTimeOffset("Day")?.UtcDateTime.ToString("ddd dd") ?? "";
                    var type = row.GetString("SentinelResourceType") ?? "";
                    var count = (int)(row.GetInt64("Failures") ?? 0);

                    if (!points.ContainsKey(day))
                        points[day] = new HealthTrendPoint { Day = day };

                    if (type.Contains("connector", StringComparison.OrdinalIgnoreCase))
                        points[day].DataConnectorFailures += count;
                    else if (type.Contains("analytic", StringComparison.OrdinalIgnoreCase))
                        points[day].AnalyticsRuleFailures += count;
                    else if (type.Contains("automation", StringComparison.OrdinalIgnoreCase))
                        points[day].AutomationFailures += count;
                    else if (type.Contains("playbook", StringComparison.OrdinalIgnoreCase))
                        points[day].PlaybookFailures += count;
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SentinelHealth trend query failed"); }
        return points.Values.ToList();
    }

    private async Task<List<ConnectorCurrentStatus>> QueryConnectorStatusAsync(LogsQueryClient client, WorkspaceContext ws)
    {
        var result = new List<ConnectorCurrentStatus>();
        try
        {
            var kql = @"
                _SentinelHealth()
                | where OperationName == 'Data fetch status change'
                | where Status in ('Success', 'Failure')
                | summarize TimeGenerated = arg_max(TimeGenerated, *) by SentinelResourceName, SentinelResourceId
                | project SentinelResourceName, SentinelResourceKind, Status, TimeGenerated, Description
                | order by Status asc, TimeGenerated desc
            ";
            var qr = await client.QueryWorkspaceAsync(ws.WorkspaceId, kql, QueryTimeRange.All);
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    result.Add(new ConnectorCurrentStatus
                    {
                        Name        = row.GetString("SentinelResourceName") ?? "",
                        Kind        = row.GetString("SentinelResourceKind") ?? "",
                        Status      = row.GetString("Status") ?? "",
                        LastEvent   = row.GetDateTimeOffset("TimeGenerated")?.UtcDateTime,
                        Description = row.GetString("Description")
                    });
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SentinelHealth connector status query failed"); }
        return result;
    }

    private async Task<List<AutoDisabledRule>> QueryAutoDisabledRulesAsync(LogsQueryClient client, WorkspaceContext ws)
    {
        var result = new List<AutoDisabledRule>();
        try
        {
            var kql = @"
                _SentinelHealth()
                | where SentinelResourceType == 'Analytics rule'
                | where Status == 'Failure'
                | where Description contains 'disabled' or Description contains 'auto-disabled'
                | summarize LastSeen = max(TimeGenerated) by SentinelResourceName, SentinelResourceId, SentinelResourceKind
                | order by LastSeen desc
            ";
            var qr = await client.QueryWorkspaceAsync(ws.WorkspaceId, kql, QueryTimeRange.All);
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    result.Add(new AutoDisabledRule
                    {
                        Name       = row.GetString("SentinelResourceName") ?? "",
                        ResourceId = row.GetString("SentinelResourceId") ?? "",
                        Kind       = row.GetString("SentinelResourceKind") ?? "",
                        LastSeen   = row.GetDateTimeOffset("LastSeen")?.UtcDateTime ?? DateTime.UtcNow
                    });
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SentinelHealth auto-disabled query failed"); }
        return result;
    }

    private async Task<List<HealthFailureEvent>> QueryRecentFailuresAsync(LogsQueryClient client, WorkspaceContext ws)
    {
        var result = new List<HealthFailureEvent>();
        try
        {
            var kql = @"
                _SentinelHealth()
                | where TimeGenerated > ago(24h)
                | where Status != 'Success'
                | project TimeGenerated, SentinelResourceType, SentinelResourceName,
                          SentinelResourceKind, Status, Description
                | order by TimeGenerated desc
                | take 50
            ";
            var qr = await client.QueryWorkspaceAsync(ws.WorkspaceId, kql, QueryTimeRange.All);
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    result.Add(new HealthFailureEvent
                    {
                        TimeGenerated = row.GetDateTimeOffset("TimeGenerated")?.UtcDateTime ?? DateTime.UtcNow,
                        ResourceType  = row.GetString("SentinelResourceType") ?? "",
                        ResourceName  = row.GetString("SentinelResourceName") ?? "",
                        Kind          = row.GetString("SentinelResourceKind") ?? "",
                        Status        = row.GetString("Status") ?? "",
                        Description   = row.GetString("Description") ?? ""
                    });
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SentinelHealth recent failures query failed"); }
        return result;
    }
}
