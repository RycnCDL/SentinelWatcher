using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class LogAnalyticsService
{
    private readonly LogsQueryClient _logsClient;
    private readonly CacheSettingsConfig _cacheConfig;
    private readonly ILogger<LogAnalyticsService> _logger;

    public LogAnalyticsService(
        LogsQueryClient logsClient,
        IOptions<CacheSettingsConfig> cacheConfig,
        ILogger<LogAnalyticsService> logger)
    {
        _logsClient = logsClient;
        _cacheConfig = cacheConfig.Value;
        _logger = logger;
    }

    // One row per incident (latest update), for incidents created in the last 30 days, then grouped
    // by rule. SecurityIncident has a row per incident update, so counting rows overstates incidents.
    // Incidents without a rule (for example from Microsoft Defender XDR) are grouped by provider.
    // TotalIncidents counts each incident once, also when it references several rules.
    private const string IncidentsKql = @"
        let incidents = materialize(
            SecurityIncident
            | where TimeGenerated > ago(30d)
            | summarize arg_max(TimeGenerated, CreatedTime, Title, ProviderName, RelatedAnalyticRuleIds) by IncidentName
            | where CreatedTime > ago(30d));
        let total = toscalar(incidents | count);
        incidents
        | extend RuleIds = iff(array_length(RelatedAnalyticRuleIds) > 0, RelatedAnalyticRuleIds, dynamic(['']))
        | mv-expand RuleId = RuleIds to typeof(string)
        | extend RuleId = tolower(tostring(split(RuleId, '/')[-1]))
        | summarize CreatedTime = take_any(CreatedTime), Title = take_any(Title), ProviderName = take_any(ProviderName)
            by IncidentName, RuleId
        | summarize IncidentCount = count(), LastIncident = max(CreatedTime), SampleTitle = take_any(Title)
            by RuleId, Provider = iff(isempty(RuleId), ProviderName, '')
        | extend TotalIncidents = total
    ";

    public async Task<AnalyticsRulesResponse> GetAnalyticsRuleStatsAsync(
        WorkspaceContext workspace,
        List<(string Id, string Name, string Severity, bool Enabled)> rules)
    {
        var groups = new List<IncidentGroup>();
        var total = 0;

        try
        {
            var result = (await _logsClient.QueryResourceAsync(
                workspace.ResourceIdentifier, IncidentsKql, LogsQueryTimeRange.All)).Value;

            foreach (var row in result.Table.Rows)
            {
                total = (int)(row.GetInt64("TotalIncidents") ?? total);
                groups.Add(new IncidentGroup(
                    RuleId: row.GetString("RuleId") ?? "",
                    Provider: row.GetString("Provider") ?? "",
                    IncidentCount: (int)(row.GetInt64("IncidentCount") ?? 0),
                    LastIncident: row.GetDateTimeOffset("LastIncident")?.UtcDateTime,
                    SampleTitle: row.GetString("SampleTitle")));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to query incidents for workspace {Workspace}", workspace.WorkspaceName);
        }

        // Titles are only sent for rules that no longer exist, as a hint to which rule it was.
        return IncidentAttribution.Build(rules, groups, total, incidentsLoaded: true);
    }

    public async Task<IngestionResponse> GetIngestionStatsAsync(WorkspaceContext workspace)
    {
        var logsClient = _logsClient;
        var response = new IngestionResponse
        {
            DaysAnalyzed = 30,
            LastRefreshed = DateTime.UtcNow
        };

        try
        {
            // Primary: Usage table (fast, pre-aggregated by Azure). Only billable data
            // (IsBillable == true) counts. Data types that appear in Usage only as free data,
            // such as SecurityAlert, are remembered so they are not estimated below either.
            var kql = @"
                Usage
                | where TimeGenerated > ago(30d)
                | summarize TotalVolumeMB = sumif(Quantity, IsBillable == true) by DataType
                | sort by TotalVolumeMB desc
            ";

            var result = await logsClient.QueryResourceAsync(workspace.ResourceIdentifier, kql, LogsQueryTimeRange.All);

            var seenInUsage = new HashSet<string>();
            if (result.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in result.Value.Table.Rows)
                {
                    var tableName = row.GetString("DataType") ?? "Unknown";
                    var volumeMB = row.GetDouble("TotalVolumeMB") ?? 0;
                    seenInUsage.Add(tableName);
                    if (volumeMB <= 0) continue; // free data only
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

            // Check important tables not found in Usage: single union query instead of N individual queries
            var importantMissing = ImportantTables.Where(t => !seenInUsage.Contains(t)).ToArray();

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

                    var missingResult = (await logsClient.QueryResourceAsync(
                        workspace.ResourceIdentifier, missingKql, LogsQueryTimeRange.All, PartialErrorsAllowed)).Value;

                    if (missingResult.Error is not null)
                        LogPartialErrorOnce(workspace, "ingestion estimate", missingResult.Error);

                    foreach (var row in missingResult.Table.Rows)
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
                            DailyAverageMB   = Math.Round(estimatedMB / 30, 2),
                            IsEstimated      = true
                        });
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
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
        "ThreatIntelligenceIndicator", "ThreatIntelIndicators", "ThreatIntelObjects",
        "SecurityEvent", "WindowsEvent",
        "McasShadowItReporting",
        "SentinelHealth", "SentinelAudit",
        "InsiderRiskEvents"
    };

    public async Task<Dictionary<string, DateTime?>> GetLastLogPerTableAsync(WorkspaceContext workspace)
    {
        var result = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // One union over the fixed table list. isfuzzy=true skips tables that don't exist in
            // the workspace; the service then reports a partial error, which is expected here.
            // The built-in Type column contains the source table name.
            var tableList = string.Join(", ", ImportantTables);
            var kql = $@"
                union isfuzzy=true {tableList}
                | where TimeGenerated > ago(24h)
                | summarize LastLog = max(TimeGenerated) by Type
                | where isnotnull(LastLog)
            ";

            var queryResult = (await _logsClient.QueryResourceAsync(
                workspace.ResourceIdentifier, kql, LogsQueryTimeRange.All, PartialErrorsAllowed)).Value;

            foreach (var row in queryResult.Table.Rows)
            {
                var tableName = row.GetString("Type") ?? "";
                var lastLog   = row.GetDateTimeOffset("LastLog")?.UtcDateTime;
                if (!string.IsNullOrEmpty(tableName) && lastLog.HasValue)
                    result[tableName] = lastLog;
            }

            if (queryResult.Error is not null)
                LogPartialErrorOnce(workspace, "last log", queryResult.Error);

            _logger.LogInformation("Found {Count} tables with data in the last 24 hours for workspace {Workspace}",
                result.Count, workspace.WorkspaceName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Last log query failed for workspace {Workspace}", workspace.WorkspaceName);
        }

        // Tables outside the fixed list, for example DCR destinations such as Perf or custom _CL
        // tables: freshness from the Usage table. Usage is aggregated per hour, so these times
        // are approximate (at most about an hour late or early).
        try
        {
            const string usageKql = @"
                Usage
                | where TimeGenerated > ago(24h)
                | summarize LastLog = max(EndTime) by DataType
            ";
            var usage = (await _logsClient.QueryResourceAsync(
                workspace.ResourceIdentifier, usageKql, LogsQueryTimeRange.All)).Value;

            var now = DateTime.UtcNow;
            foreach (var row in usage.Table.Rows)
            {
                var tableName = row.GetString("DataType") ?? "";
                var lastLog   = row.GetDateTimeOffset("LastLog")?.UtcDateTime;
                if (string.IsNullOrEmpty(tableName) || !lastLog.HasValue || result.ContainsKey(tableName)) continue;
                result[tableName] = lastLog.Value > now ? now : lastLog.Value;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Usage freshness query failed for workspace {Workspace}", workspace.WorkspaceName);
        }

        return result;
    }

    // Partial errors from "union isfuzzy=true" (tables that don't exist) are expected. Without this
    // option the SDK throws instead of returning the rows that were found.
    private static readonly LogsQueryOptions PartialErrorsAllowed = new() { AllowPartialErrors = true };

    private static readonly System.Text.RegularExpressions.Regex MissingTablePattern =
        new(@"named '([A-Za-z0-9_]+)'", System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _loggedPartialErrors = new();

    /// <summary>
    /// Logs a partial query error once per workspace, query and message, at Information level,
    /// because missing tables are normal (not every workspace has every connector).
    /// </summary>
    private void LogPartialErrorOnce(WorkspaceContext workspace, string queryName, object? error)
    {
        var text = error?.ToString() ?? "unknown partial error";
        if (!_loggedPartialErrors.TryAdd($"{workspace.Key}|{queryName}|{text}", 0)) return;

        var missing = MissingTablePattern.Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (missing.Count > 0)
            _logger.LogInformation("Tables not present in workspace {Workspace}, skipped by the {Query} query: {Tables}",
                workspace.WorkspaceName, queryName, string.Join(", ", missing));
        else
            _logger.LogInformation("The {Query} query returned a partial result for workspace {Workspace}: {Error}",
                queryName, workspace.WorkspaceName, text);
    }

    public async Task<SentinelHealthResponse> GetSentinelHealthAsync(WorkspaceContext workspace, int days = 7)
    {
        var logsClient = _logsClient;
        var response = new SentinelHealthResponse { QueryTime = DateTime.UtcNow };

        // Quick probe: does the workspace have any SentinelHealth data?
        try
        {
            var probeKql = "_SentinelHealth() | take 1 | project TimeGenerated";
            var probe = await logsClient.QueryResourceAsync(workspace.ResourceIdentifier, probeKql, LogsQueryTimeRange.All);
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
        var connectorsTask    = QueryConnectorStatusAsync(logsClient, workspace, days);
        var autoDisabledTask  = QueryAutoDisabledRulesAsync(logsClient, workspace);
        var recentFailTask    = QueryRecentFailuresAsync(logsClient, workspace);

        await Task.WhenAll(summaryTask, trendTask, connectorsTask, autoDisabledTask, recentFailTask);

        // Populate response from results
        var summary = summaryTask.Result;
        var rules       = summary.GetValueOrDefault(SentinelHealthScore.AnalyticsRule);
        var automation  = summary.GetValueOrDefault(SentinelHealthScore.AutomationRule);
        var playbooks   = summary.GetValueOrDefault(SentinelHealthScore.Playbook);
        response.TotalRuleRuns        = rules.Total;
        response.FailedRuleRuns       = rules.Failure;
        response.TotalAutomationRuns  = automation.Total + playbooks.Total;
        response.FailedAutomationRuns = automation.Failure + playbooks.Failure;

        response.Trend               = trendTask.Result;
        response.ConnectorStatuses   = connectorsTask.Result;
        response.AutoDisabledRules   = autoDisabledTask.Result;
        response.AutoDisabledRulesCount = response.AutoDisabledRules.Count;
        response.RecentFailures      = recentFailTask.Result;

        // Connector health KPI: latest reported status per connector. A connector without a health
        // event in the selected range is stale and does not count as healthy.
        response.TotalConnectors   = response.ConnectorStatuses.Count;
        response.HealthyConnectors = response.ConnectorStatuses.Count(c => c.Status == "Success" && !c.IsStale);
        response.StaleConnectors   = response.ConnectorStatuses.Count(c => c.IsStale);

        // Health Score: % of health events in the last 24h that succeeded, over all resource types.
        response.HealthScore = SentinelHealthScore.Compute(summary.Values);

        return response;
    }

    // ── Helper queries ────────────────────────────────────────────────────────

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _loggedHealthDescriptions = new();

    /// <summary>
    /// Health event descriptions can contain account names, resource names or URLs, so the browser
    /// only gets a category. The raw text of non-success events is logged here, once per resource
    /// and text.
    /// </summary>
    private void LogHealthDescriptionOnce(WorkspaceContext ws, string resourceType, string resourceName, string status, string? description)
    {
        if (string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(description))
            return;
        if (!_loggedHealthDescriptions.TryAdd($"{ws.Key}|{resourceType}|{resourceName}|{status}|{description}", 0))
            return;

        _logger.LogInformation("Sentinel health {Status} for {ResourceType} '{ResourceName}' in workspace {Workspace}: {Description}",
            status, resourceType, resourceName, ws.WorkspaceName, description);
    }

    private async Task<Dictionary<string, HealthRunCounts>> QuerySummaryAsync(LogsQueryClient client, WorkspaceContext ws)
    {
        var result = SentinelHealthScore.CreateSummary();
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
            var qr = await client.QueryResourceAsync(ws.ResourceIdentifier, kql, LogsQueryTimeRange.All);
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    var type = SentinelHealthScore.NormalizeResourceType(row.GetString("SentinelResourceType"));
                    result[type] = new HealthRunCounts(
                        Total:   (int)(row.GetInt64("Total") ?? 0),
                        Success: (int)(row.GetInt64("Success") ?? 0),
                        Failure: (int)(row.GetInt64("Failure") ?? 0));
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
            // The window is passed as the query time range parameter, not interpolated into the KQL.
            const string kql = @"
                _SentinelHealth()
                | where Status != 'Success'
                | summarize Failures = count() by SentinelResourceType, Day = bin(TimeGenerated, 1d)
                | order by Day asc
            ";
            var qr = await client.QueryResourceAsync(
                ws.ResourceIdentifier, kql, new LogsQueryTimeRange(TimeSpan.FromDays(days)));
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    // ISO date (yyyy-MM-dd); the client formats it for display.
                    var day = row.GetDateTimeOffset("Day")?.UtcDateTime.ToString("yyyy-MM-dd") ?? "";
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

    private async Task<List<ConnectorCurrentStatus>> QueryConnectorStatusAsync(LogsQueryClient client, WorkspaceContext ws, int days)
    {
        var result = new List<ConnectorCurrentStatus>();
        try
        {
            // Latest status per connector. Grouping by name and kind (not resource ID) keeps one row
            // per connector when its resource ID changed, for example after it was reconnected.
            var kql = @"
                _SentinelHealth()
                | where SentinelResourceType == 'Data connector'
                | where Status in ('Success', 'Failure')
                | summarize arg_max(TimeGenerated, Status, Description) by SentinelResourceName, SentinelResourceKind
                | order by Status asc, TimeGenerated desc
            ";
            var qr = await client.QueryResourceAsync(ws.ResourceIdentifier, kql, LogsQueryTimeRange.All);
            var staleBefore = DateTime.UtcNow.AddDays(-days);
            foreach (var row in qr.Value.Table.Rows)
            {
                var name   = row.GetString("SentinelResourceName") ?? "";
                var status = row.GetString("Status") ?? "";
                var last   = row.GetDateTimeOffset("TimeGenerated")?.UtcDateTime;
                var description = row.GetString("Description");
                LogHealthDescriptionOnce(ws, "Data connector", name, status, description);

                result.Add(new ConnectorCurrentStatus
                {
                    Name      = name,
                    Kind      = row.GetString("SentinelResourceKind") ?? "",
                    Status    = status,
                    LastEvent = last,
                    IsStale   = !last.HasValue || last.Value < staleBefore,
                    Category  = HealthFailureCategory.Categorize(status, description),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "SentinelHealth connector status query failed");
        }
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
            var qr = await client.QueryResourceAsync(ws.ResourceIdentifier, kql, LogsQueryTimeRange.All);
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
            var qr = await client.QueryResourceAsync(ws.ResourceIdentifier, kql, LogsQueryTimeRange.All);
            if (qr.Value.Status == LogsQueryResultStatus.Success)
            {
                foreach (var row in qr.Value.Table.Rows)
                {
                    var resourceType = row.GetString("SentinelResourceType") ?? "";
                    var resourceName = row.GetString("SentinelResourceName") ?? "";
                    var status       = row.GetString("Status") ?? "";
                    var description  = row.GetString("Description");
                    LogHealthDescriptionOnce(ws, resourceType, resourceName, status, description);

                    result.Add(new HealthFailureEvent
                    {
                        TimeGenerated = row.GetDateTimeOffset("TimeGenerated")?.UtcDateTime ?? DateTime.UtcNow,
                        ResourceType  = resourceType,
                        ResourceName  = resourceName,
                        Kind          = row.GetString("SentinelResourceKind") ?? "",
                        Status        = status,
                        Category      = HealthFailureCategory.Categorize(status, description) ?? HealthFailureCategory.Other
                    });
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "SentinelHealth recent failures query failed"); }
        return result;
    }
}
