using System.Text.Json;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class SentinelService
{
    private readonly ArmRestClient _arm;
    private readonly ConnectorThresholds _thresholds;
    private readonly ILogger<SentinelService> _logger;

    private const string ApiVersion = "2024-03-01";

    // Comprehensive table-to-connector mapping for detecting data sources from actual log flow.
    // This is the primary detection method since Content Hub connectors (Defender XDR, Entra ID, etc.)
    // don't appear in the traditional /dataConnectors ARM API.
    private static readonly Dictionary<string, (string ConnectorName, string ConnectorType)> TableToConnectorMap = new()
    {
        // Microsoft Defender XDR (M365 Defender)
        { "DeviceEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "DeviceLogonEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "DeviceProcessEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "DeviceNetworkEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "DeviceFileEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "DeviceRegistryEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "DeviceImageLoadEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "DeviceInfo", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "AlertEvidence", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "AlertInfo", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "EmailEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "EmailAttachmentInfo", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "EmailUrlInfo", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "EmailPostDeliveryEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "IdentityLogonEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "IdentityQueryEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "IdentityDirectoryEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "CloudAppEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },
        { "UrlClickEvents", ("Microsoft Defender XDR", "MicrosoftThreatProtection") },

        // Microsoft Entra ID (Azure AD)
        { "SigninLogs", ("Microsoft Entra ID", "AzureActiveDirectory") },
        { "AuditLogs", ("Microsoft Entra ID", "AzureActiveDirectory") },
        { "AADNonInteractiveUserSignInLogs", ("Microsoft Entra ID", "AzureActiveDirectory") },
        { "AADServicePrincipalSignInLogs", ("Microsoft Entra ID", "AzureActiveDirectory") },
        { "AADManagedIdentitySignInLogs", ("Microsoft Entra ID", "AzureActiveDirectory") },
        { "AADProvisioningLogs", ("Microsoft Entra ID", "AzureActiveDirectory") },
        { "AADRiskyUsers", ("Microsoft Entra ID", "AzureActiveDirectory") },
        { "AADUserRiskEvents", ("Microsoft Entra ID", "AzureActiveDirectory") },

        // Microsoft Entra ID Protection
        { "AADRiskyServicePrincipals", ("Microsoft Entra ID Protection", "AzureADIdentityProtection") },
        { "SecurityAlert", ("Microsoft Defender for Cloud", "AzureSecurityCenter") },

        // Microsoft Defender for Cloud Apps
        { "McasShadowItReporting", ("Microsoft Defender for Cloud Apps", "MicrosoftCloudAppSecurity") },

        // Microsoft Defender for Endpoint (standalone tables if not part of XDR)
        { "DeviceTvmSoftwareInventory", ("Microsoft Defender for Endpoint", "MicrosoftDefenderAdvancedThreatProtection") },
        { "DeviceTvmSoftwareVulnerabilities", ("Microsoft Defender for Endpoint", "MicrosoftDefenderAdvancedThreatProtection") },

        // Office 365
        { "OfficeActivity", ("Office 365", "Office365") },

        // Azure Activity
        { "AzureActivity", ("Azure Activity", "AzureActivity") },

        // Windows events. SecurityEvent is written by "Windows Security Events via AMA" (and was
        // written by the legacy Log Analytics agent, which is retired). WindowsEvent is written by
        // "Windows Forwarded Events".
        { "SecurityEvent", ("Windows Security Events", "WindowsSecurityEvents") },
        { "WindowsEvent", ("Windows Forwarded Events", "WindowsForwardedEvents") },

        // Syslog / CEF
        { "Syslog", ("Syslog", "Syslog") },
        { "CommonSecurityLog", ("Common Event Format (CEF)", "CommonEventFormat") },

        // Threat Intelligence: the legacy table plus the tables that replace it in migrated workspaces
        { "ThreatIntelligenceIndicator", ("Threat Intelligence", "ThreatIntelligence") },
        { "ThreatIntelIndicators", ("Threat Intelligence", "ThreatIntelligence") },
        { "ThreatIntelObjects", ("Threat Intelligence", "ThreatIntelligence") },

        // DNS
        { "DnsEvents", ("DNS", "DNS") },
        { "DnsInventory", ("DNS", "DNS") },

        // Microsoft Sentinel internal
        { "SecurityIncident", ("Microsoft Sentinel", "Sentinel") },
        { "SentinelHealth", ("Microsoft Sentinel", "Sentinel") },
        { "SentinelAudit", ("Microsoft Sentinel", "Sentinel") },

        // Microsoft 365 Insider Risk Management
        { "InsiderRiskEvents", ("Microsoft 365 Insider Risk Management", "OfficeIRM") },

        // Azure Firewall
        { "AZFWApplicationRule", ("Azure Firewall", "AzureFirewall") },
        { "AZFWNetworkRule", ("Azure Firewall", "AzureFirewall") },
        { "AZFWNatRule", ("Azure Firewall", "AzureFirewall") },
        { "AZFWThreatIntel", ("Azure Firewall", "AzureFirewall") },
        { "AZFWIdpsSignature", ("Azure Firewall", "AzureFirewall") },

        // Azure Diagnostics (generic)
        { "AzureDiagnostics", ("Azure Diagnostics", "AzureDiagnostics") },

        // SAP
        { "ABAPAuditLog_CL", ("SAP", "SAP") },
        { "SAPcon_CL", ("SAP", "SAP") },

        // Windows Firewall
        { "WindowsFirewall", ("Windows Firewall", "WindowsFirewall") },

        // Microsoft Purview
        { "MicrosoftPurviewInformationProtection", ("Microsoft Purview", "MicrosoftPurview") },
    };

    public SentinelService(
        ArmRestClient arm,
        ConnectorThresholds thresholds,
        ILogger<SentinelService> logger)
    {
        _arm = arm;
        _thresholds = thresholds;
        _logger = logger;
    }

    public async Task<List<(string Id, string Name, string Severity, bool Enabled)>> GetAnalyticsRulesAsync(WorkspaceContext workspace)
    {
        var rules = new List<(string Id, string Name, string Severity, bool Enabled)>();
        try
        {
            var allItems = await _arm.GetAllPagesAsync(
                $"{workspace.ResourceId}/providers/Microsoft.SecurityInsights/alertRules", ApiVersion);

            foreach (var item in allItems)
            {
                var id = item.GetProperty("id").GetString() ?? "";
                var props = item.GetProperty("properties");

                var name     = props.TryGetProperty("displayName", out var dn)  ? dn.GetString()  ?? "Unknown" : "Unknown";
                var severity = props.TryGetProperty("severity",    out var sev) ? sev.GetString() ?? "Unknown" : "Unknown";
                var enabled  = props.TryGetProperty("enabled",     out var en)  && en.GetBoolean();

                rules.Add((id, name, severity, enabled));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list analytics rules for workspace {Ws}", workspace.WorkspaceName);
        }
        return rules;
    }

    public async Task<DataConnectorsResponse> GetDataConnectorsAsync(
        WorkspaceContext workspace,
        Dictionary<string, DateTime?> lastLogPerTable,
        IReadOnlyDictionary<string, string> tableTiers)
    {
        var response = new DataConnectorsResponse
        {
            LastChecked = DateTime.UtcNow
        };

        try
        {
            // Primary detection: Build connectors from actual table data.
            var connectorGroups = new Dictionary<string, ConnectorHealth>();

            foreach (var (tableName, lastLog) in lastLogPerTable)
            {
                if (!TableToConnectorMap.TryGetValue(tableName, out var connectorInfo))
                    continue;

                var key = connectorInfo.ConnectorName;
                if (!connectorGroups.TryGetValue(key, out var health))
                {
                    health = new ConnectorHealth
                    {
                        ConnectorId = key.ToLower().Replace(" ", "-"),
                        ConnectorName = connectorInfo.ConnectorName,
                        ConnectorType = connectorInfo.ConnectorType,
                        AssociatedTables = new List<string>()
                    };
                    connectorGroups[key] = health;
                }

                health.AssociatedTables.Add(tableName);

                if (lastLog.HasValue)
                {
                    if (!health.LastLogReceived.HasValue || lastLog.Value > health.LastLogReceived.Value)
                        health.LastLogReceived = lastLog.Value;
                }
            }

            _logger.LogInformation("Table-based detection found {Count} connectors from {Tables} active tables",
                connectorGroups.Count, lastLogPerTable.Count);

            // Secondary: Also try ARM API for any additional traditional connectors
            try
            {
                var allItems = await _arm.GetAllPagesAsync(
                    $"{workspace.ResourceId}/providers/Microsoft.SecurityInsights/dataConnectors", ApiVersion);

                _logger.LogInformation("ARM API returned {Count} traditional connectors", allItems.Count);

                foreach (var item in allItems)
                {
                    var kind = item.TryGetProperty("kind", out var kindProp) ? kindProp.GetString() ?? "Unknown" : "Unknown";
                    var id = item.GetProperty("id").GetString() ?? "";
                    var name = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Unknown" : "Unknown";

                    if (item.TryGetProperty("properties", out var props) &&
                        props.TryGetProperty("displayName", out var displayName))
                    {
                        name = displayName.GetString() ?? name;
                    }

                    // Skip connectors already shown from table data: same type, or every table
                    // the connector writes to is already listed under a detected connector.
                    var mappedTables = MapConnectorKindToTables(kind);
                    var alreadyDetected =
                        connectorGroups.Values.Any(c => c.ConnectorType == kind) ||
                        (mappedTables.Count > 0 && mappedTables.All(t =>
                            connectorGroups.Values.Any(c => c.AssociatedTables.Contains(t))));
                    if (alreadyDetected)
                        continue;

                    var connHealth = new ConnectorHealth
                    {
                        ConnectorId = id,
                        ConnectorName = name,
                        ConnectorType = kind,
                        AssociatedTables = mappedTables
                    };

                    DateTime? latestLog = null;
                    foreach (var table in mappedTables)
                    {
                        if (lastLogPerTable.TryGetValue(table, out var lastLog) && lastLog.HasValue)
                        {
                            if (!latestLog.HasValue || lastLog.Value > latestLog.Value)
                                latestLog = lastLog.Value;
                        }
                    }
                    connHealth.LastLogReceived = latestLog;

                    connectorGroups[name] = connHealth;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ARM API connector query failed, using table-based detection only");
            }

            // Calculate health status
            var now = DateTime.UtcNow;
            foreach (var health in connectorGroups.Values)
            {
                ApplyHealthStatus(health, tableTiers, _thresholds.For(health.ConnectorType), now);
                response.Connectors.Add(health);
            }

            response.Connectors = response.Connectors
                .OrderBy(c => c.Status switch { "Error" => 0, "Warning" => 1, "Healthy" => 2, "Quiet" => 3, _ => 4 })
                .ThenBy(c => c.ConnectorName)
                .ToList();

            response.HealthyCount = response.Connectors.Count(c => c.Status == "Healthy");
            response.WarningCount = response.Connectors.Count(c => c.Status == "Warning");
            response.ErrorCount = response.Connectors.Count(c => c.Status == "Error");
            response.QuietCount = response.Connectors.Count(c => c.Status == "Quiet");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect data connectors");
        }

        return response;
    }

    public const string DataLakeMessage =
        "Not available in data lake tier: last log times can't be read through the Log Analytics query API";

    public const string QuietMessageSuffix = "This connector only sends data when events happen.";

    /// <summary>
    /// Sets Status, GapMinutes and ErrorMessage from the newest record in the connector's tables:
    /// Healthy up to the warning threshold, then Warning, then Error. Event-driven connectors are
    /// Quiet instead of Warning or Error. A connector whose tables are all in the data lake tier is
    /// Unknown, because its last log time can't be read.
    /// </summary>
    public static void ApplyHealthStatus(
        ConnectorHealth health, IReadOnlyDictionary<string, string> tableTiers, ConnectorThreshold threshold, DateTime utcNow)
    {
        health.DataLakeTables = health.AssociatedTables.Where(t => TableTiers.IsDataLake(tableTiers, t)).ToList();
        health.ErrorMessage = null;

        if (health.LastLogReceived.HasValue)
        {
            var gap = Math.Max(0, (utcNow - health.LastLogReceived.Value).TotalMinutes);
            health.GapMinutes = Math.Round(gap, 1);

            if (gap <= threshold.WarningAfterMinutes)
            {
                health.Status = "Healthy";
            }
            else if (threshold.EventDriven)
            {
                health.Status = "Quiet";
                health.ErrorMessage = $"No events for {FormatGap(gap)}. {QuietMessageSuffix}";
            }
            else
            {
                health.Status = gap <= threshold.ErrorAfterMinutes ? "Warning" : "Error";
                health.ErrorMessage = $"No logs received for {FormatGap(gap)}";
            }
        }
        else if (health.AssociatedTables.Count == 0)
        {
            health.Status = "Unknown";
            health.ErrorMessage = "No associated tables identified";
        }
        else if (health.DataLakeTables.Count == health.AssociatedTables.Count)
        {
            health.Status = "Unknown";
            health.ErrorMessage = DataLakeMessage;
        }
        else if (threshold.EventDriven)
        {
            health.Status = "Quiet";
            health.ErrorMessage = $"No events in the last 24 hours. {QuietMessageSuffix}";
        }
        else
        {
            health.Status = "Error";
            health.ErrorMessage = "No logs received in the last 24 hours";
        }
    }

    private static string FormatGap(double minutes) =>
        minutes < 120 ? $"{Math.Round(minutes)} minutes" : $"{Math.Round(minutes / 60, 1)} hours";

    /// <summary>Connector that writes to the given table, if the table is known.</summary>
    public static bool TryMapTableToConnector(string tableName, out (string ConnectorName, string ConnectorType) connector) =>
        TableToConnectorMap.TryGetValue(tableName, out connector);

    /// <summary>Tables written by an ARM data connector of the given kind.</summary>
    public static List<string> MapConnectorKindToTables(string kind)
    {
        return kind switch
        {
            "AzureActiveDirectory" => new List<string> { "SigninLogs", "AuditLogs" },
            "AzureAdvancedThreatProtection" => new List<string> { "SecurityAlert" },
            "MicrosoftDefenderAdvancedThreatProtection" => new List<string> { "SecurityAlert", "DeviceEvents" },
            "Office365" => new List<string> { "OfficeActivity" },
            "ThreatIntelligence" or "ThreatIntelligenceTaxii" or "MicrosoftThreatIntelligence" => ThreatIntelTables(),
            "AmazonWebServicesCloudTrail" => new List<string> { "AWSCloudTrail" },
            "AmazonWebServicesS3" => new List<string> { "AWSCloudTrail", "AWSGuardDuty", "AWSVPCFlow" },
            "MicrosoftCloudAppSecurity" => new List<string> { "McasShadowItReporting", "SecurityAlert" },
            "Syslog" or "CEF" or "GenericUI" => new List<string> { "Syslog", "CommonSecurityLog" },
            "WindowsFirewall" => new List<string> { "WindowsFirewall" },
            "SecurityEvents" or "WindowsSecurityEvents" => new List<string> { "SecurityEvent" },
            "WindowsForwardedEvents" => new List<string> { "WindowsEvent" },
            "AzureSecurityCenter" => new List<string> { "SecurityAlert" },
            "MicrosoftThreatProtection" => new List<string> { "DeviceEvents", "DeviceLogonEvents", "DeviceProcessEvents" },
            "AzureActivity" => new List<string> { "AzureActivity" },
            "DNS" => new List<string> { "DnsEvents", "DnsInventory" },
            _ => new List<string>()
        };
    }

    // Workspaces migrated to the new threat intelligence schema write ThreatIntelIndicators and
    // ThreatIntelObjects instead of ThreatIntelligenceIndicator. Any of them counts as data.
    private static List<string> ThreatIntelTables() =>
        new() { "ThreatIntelligenceIndicator", "ThreatIntelIndicators", "ThreatIntelObjects" };
}
