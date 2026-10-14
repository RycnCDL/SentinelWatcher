using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

public class SentinelService
{
    private readonly HttpClient _httpClient;
    private readonly UserAzureClientFactory _clientFactory;
    private readonly CacheSettingsConfig _cacheConfig;
    private readonly ILogger<SentinelService> _logger;

    private const string ArmBaseUrl = "https://management.azure.com";
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

        // Security Events (Windows)
        { "SecurityEvent", ("Security Events via Legacy Agent", "SecurityEvents") },
        { "WindowsEvent", ("Windows Security Events via AMA", "WindowsSecurityEvents") },

        // Syslog / CEF
        { "Syslog", ("Syslog", "Syslog") },
        { "CommonSecurityLog", ("Common Event Format (CEF)", "CommonEventFormat") },

        // Threat Intelligence
        { "ThreatIntelligenceIndicator", ("Threat Intelligence", "ThreatIntelligence") },

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

    private readonly ResourceGraphService _resourceGraph;

    public SentinelService(
        HttpClient httpClient,
        UserAzureClientFactory clientFactory,
        ResourceGraphService resourceGraph,
        IOptions<CacheSettingsConfig> cacheConfig,
        ILogger<SentinelService> logger)
    {
        _httpClient = httpClient;
        _clientFactory = clientFactory;
        _resourceGraph = resourceGraph;
        _cacheConfig = cacheConfig.Value;
        _logger = logger;
    }

    private static string GetWorkspacePath(WorkspaceContext workspace)
    {
        return $"/subscriptions/{workspace.SubscriptionId}/resourceGroups/{workspace.ResourceGroup}" +
               $"/providers/Microsoft.OperationalInsights/workspaces/{workspace.WorkspaceName}";
    }

    public async Task<List<(string Id, string Name, string Severity, bool Enabled)>> GetAnalyticsRulesAsync(WorkspaceContext workspace)
    {
        // Try Azure Resource Graph first: single round-trip vs paginated ARM REST calls.
        var rules = await _resourceGraph.GetAnalyticsRulesAsync(workspace);
        if (rules.Count > 0)
        {
            _logger.LogInformation("Analytics rules loaded via ARG ({Count} rules)", rules.Count);
            return rules;
        }

        // Fallback to direct ARM REST API (e.g. if ARG hasn't indexed the resources yet).
        _logger.LogWarning("ARG returned 0 rules for {Ws}, falling back to ARM REST API", workspace.WorkspaceName);
        return await GetAnalyticsRulesFromArmAsync(workspace);
    }

    private async Task<List<(string Id, string Name, string Severity, bool Enabled)>> GetAnalyticsRulesFromArmAsync(WorkspaceContext workspace)
    {
        var rules = new List<(string Id, string Name, string Severity, bool Enabled)>();
        try
        {
            var token = await _clientFactory.GetArmAccessTokenAsync();
            var url = $"{ArmBaseUrl}{GetWorkspacePath(workspace)}/providers/Microsoft.SecurityInsights/alertRules?api-version={ApiVersion}";

            var allItems = await GetAllPagesAsync(url, token);

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
            _logger.LogError(ex, "ARM fallback also failed for analytics rules");
        }
        return rules;
    }

    public async Task<DataConnectorsResponse> GetDataConnectorsAsync(WorkspaceContext workspace, Dictionary<string, DateTime?> lastLogPerTable)
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
                var token = await _clientFactory.GetArmAccessTokenAsync();
                var url = $"{ArmBaseUrl}{GetWorkspacePath(workspace)}/providers/Microsoft.SecurityInsights/dataConnectors?api-version={ApiVersion}";
                var allItems = await GetAllPagesAsync(url, token);

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

                    var alreadyDetected = connectorGroups.Values.Any(c => c.ConnectorType == kind);
                    if (alreadyDetected)
                        continue;

                    var mappedTables = MapConnectorKindToTables(kind);
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
            foreach (var health in connectorGroups.Values)
            {
                if (health.LastLogReceived.HasValue)
                {
                    var gap = (DateTime.UtcNow - health.LastLogReceived.Value).TotalMinutes;
                    health.GapMinutes = Math.Round(gap, 1);

                    if (gap <= _cacheConfig.ConnectorGapThresholdMinutes)
                        health.Status = "Healthy";
                    else if (gap <= _cacheConfig.ConnectorGapThresholdMinutes * 2)
                    {
                        health.Status = "Warning";
                        health.ErrorMessage = $"No logs received for {Math.Round(gap)} minutes";
                    }
                    else
                    {
                        health.Status = "Error";
                        health.ErrorMessage = $"No logs received for {Math.Round(gap)} minutes";
                    }
                }
                else
                {
                    health.Status = health.AssociatedTables.Count > 0 ? "Error" : "Unknown";
                    health.ErrorMessage = health.AssociatedTables.Count > 0
                        ? "No logs received in the last 24 hours"
                        : "No associated tables identified";
                }

                response.Connectors.Add(health);
            }

            response.Connectors = response.Connectors
                .OrderBy(c => c.Status == "Error" ? 0 : c.Status == "Warning" ? 1 : c.Status == "Healthy" ? 2 : 3)
                .ToList();

            response.HealthyCount = response.Connectors.Count(c => c.Status == "Healthy");
            response.WarningCount = response.Connectors.Count(c => c.Status == "Warning");
            response.ErrorCount = response.Connectors.Count(c => c.Status == "Error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect data connectors");
        }

        return response;
    }

    private async Task<List<JsonElement>> GetAllPagesAsync(string url, string token)
    {
        var items = new List<JsonElement>();
        var currentUrl = url;

        while (!string.IsNullOrEmpty(currentUrl))
        {
            var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var httpResponse = await _httpClient.SendAsync(request);
            httpResponse.EnsureSuccessStatusCode();

            var json = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var item in values.EnumerateArray())
                    items.Add(item.Clone());
            }

            currentUrl = doc.RootElement.TryGetProperty("nextLink", out var nextLink)
                ? nextLink.GetString()
                : null;
        }

        return items;
    }

    private static List<string> MapConnectorKindToTables(string kind)
    {
        return kind switch
        {
            "AzureActiveDirectory" => new List<string> { "SigninLogs", "AuditLogs" },
            "AzureAdvancedThreatProtection" => new List<string> { "SecurityAlert" },
            "MicrosoftDefenderAdvancedThreatProtection" => new List<string> { "SecurityAlert", "DeviceEvents" },
            "Office365" => new List<string> { "OfficeActivity" },
            "ThreatIntelligence" or "ThreatIntelligenceTaxii" => new List<string> { "ThreatIntelligenceIndicator" },
            "AmazonWebServicesCloudTrail" => new List<string> { "AWSCloudTrail" },
            "AmazonWebServicesS3" => new List<string> { "AWSCloudTrail", "AWSGuardDuty", "AWSVPCFlow" },
            "MicrosoftCloudAppSecurity" => new List<string> { "McasShadowItReporting", "SecurityAlert" },
            "Syslog" or "CEF" or "GenericUI" => new List<string> { "Syslog", "CommonSecurityLog" },
            "WindowsFirewall" => new List<string> { "WindowsFirewall" },
            "SecurityEvents" or "WindowsSecurityEvents" => new List<string> { "SecurityEvent" },
            "AzureSecurityCenter" => new List<string> { "SecurityAlert" },
            "MicrosoftThreatProtection" => new List<string> { "DeviceEvents", "DeviceLogonEvents", "DeviceProcessEvents" },
            "MicrosoftThreatIntelligence" => new List<string> { "ThreatIntelligenceIndicator" },
            "AzureActivity" => new List<string> { "AzureActivity" },
            "DNS" => new List<string> { "DnsEvents", "DnsInventory" },
            _ => new List<string>()
        };
    }
}
