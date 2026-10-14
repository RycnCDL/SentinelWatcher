using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

/// <summary>
/// Queries Azure Resource Graph for ARM resource configuration metadata.
/// A single ARG round-trip replaces N+1 per-subscription ARM REST calls.
///
/// Scope: configuration data only (enabled state, severity, provisioning state, etc.)
/// Operational data (incident counts, log volumes, connector health gaps) still
/// requires Log Analytics KQL queries via LogAnalyticsService.
/// </summary>
public class ResourceGraphService
{
    private readonly HttpClient _httpClient;
    private readonly UserAzureClientFactory _clientFactory;
    private readonly ILogger<ResourceGraphService> _logger;

    private const string ArgEndpoint =
        "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01";

    private const string ArmSubscriptionsUrl =
        "https://management.azure.com/subscriptions?api-version=2022-12-01";

    public ResourceGraphService(
        HttpClient httpClient,
        UserAzureClientFactory clientFactory,
        ILogger<ResourceGraphService> logger)
    {
        _httpClient = httpClient;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    // ── Subscriptions ─────────────────────────────────────────────────────────

    /// <summary>Returns all subscriptions (id + display name) the current user can access.</summary>
    public async Task<List<(string Id, string Name)>> GetSubscriptionsAsync()
    {
        var subs = new List<(string Id, string Name)>();
        try
        {
            var token = await _clientFactory.GetArmAccessTokenAsync();
            var request = new HttpRequestMessage(HttpMethod.Get, ArmSubscriptionsUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var sub in values.EnumerateArray())
                {
                    var id   = sub.GetProperty("subscriptionId").GetString() ?? "";
                    var name = sub.GetProperty("displayName").GetString()    ?? "";
                    if (!string.IsNullOrEmpty(id))
                        subs.Add((id, name));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate subscriptions");
        }

        return subs;
    }

    // ── Core ARG Query ────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a KQL query against Azure Resource Graph across the given subscriptions.
    /// Automatically handles pagination via skipToken for result sets larger than 1000 rows.
    /// </summary>
    public async Task<List<JsonElement>> QueryAsync(string kql, IList<string> subscriptionIds)
    {
        var results = new List<JsonElement>();
        string? skipToken = null;

        var token = await _clientFactory.GetArmAccessTokenAsync();

        do
        {
            var body    = BuildRequestBody(kql, subscriptionIds, skipToken);
            var request = new HttpRequestMessage(HttpMethod.Post, ArgEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var httpResponse = await _httpClient.SendAsync(request);
            if (!httpResponse.IsSuccessStatusCode)
            {
                var err = await httpResponse.Content.ReadAsStringAsync();
                _logger.LogError("ARG query failed ({Status}): {Body}", httpResponse.StatusCode, err);
                break;
            }

            var json = await httpResponse.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("data", out var data))
                foreach (var item in data.EnumerateArray())
                    results.Add(item.Clone());

            skipToken = doc.RootElement.TryGetProperty("$skipToken", out var st)
                ? st.GetString()
                : null;

        } while (!string.IsNullOrEmpty(skipToken));

        return results;
    }

    // ── High-level helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Discovers all Log Analytics workspaces with Sentinel enabled across all
    /// accessible subscriptions in a single ARG query.
    /// Replaces the N+1 subscription/workspace/probe pattern in WorkspaceService.
    /// </summary>
    public async Task<List<WorkspaceInfo>> GetSentinelWorkspacesAsync()
    {
        var workspaces = new List<WorkspaceInfo>();
        try
        {
            var subs = await GetSubscriptionsAsync();
            if (subs.Count == 0) return workspaces;

            var subIds   = subs.Select(s => s.Id).ToList();
            var subNames = subs.ToDictionary(s => s.Id, s => s.Name, StringComparer.OrdinalIgnoreCase);

            // Join Log Analytics workspaces with Sentinel solution resources in one round-trip.
            // Sentinel is installed as a Microsoft.OperationsManagement/solutions resource
            // named "SecurityInsights({workspaceName})" on the workspace.
            var kql =
                "Resources" +
                " | where type =~ \"microsoft.operationalinsights/workspaces\"" +
                " | extend wsArmId = tolower(id)" +
                " | join kind=inner (" +
                "     Resources" +
                "     | where type =~ \"microsoft.operationsmanagement/solutions\"" +
                "     | where tolower(name) startswith \"securityinsights(\"" +
                "     | extend wsArmId = tolower(tostring(properties.workspaceResourceId))" +
                "     | project wsArmId" +
                " ) on wsArmId" +
                " | project subscriptionId, resourceGroup, name, location," +
                "           workspaceId = tostring(properties.customerId)";

            var items = await QueryAsync(kql, subIds);

            foreach (var item in items)
            {
                var subId = item.TryGetProperty("subscriptionId", out var s) ? s.GetString() ?? "" : "";
                workspaces.Add(new WorkspaceInfo
                {
                    SubscriptionId   = subId,
                    SubscriptionName = subNames.TryGetValue(subId, out var n) ? n : "",
                    ResourceGroup    = item.TryGetProperty("resourceGroup", out var rg)  ? rg.GetString()   ?? "" : "",
                    WorkspaceName    = item.TryGetProperty("name",          out var nm)  ? nm.GetString()   ?? "" : "",
                    Location         = item.TryGetProperty("location",      out var loc) ? loc.GetString()  ?? "" : "",
                    WorkspaceId      = item.TryGetProperty("workspaceId",   out var wsId)? wsId.GetString() ?? "" : "",
                    SentinelEnabled  = true
                });
            }

            _logger.LogInformation("ARG discovered {Count} Sentinel-enabled workspaces", workspaces.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ARG workspace discovery failed");
        }

        return workspaces;
    }

    /// <summary>
    /// Returns analytics rule config (id, displayName, severity, enabled) for a workspace via ARG.
    /// Single round-trip replaces paginated ARM REST calls to the /alertRules endpoint.
    /// NOTE: Incident counts and rule run history still require Log Analytics KQL queries.
    /// </summary>
    public async Task<List<(string Id, string Name, string Severity, bool Enabled)>> GetAnalyticsRulesAsync(
        WorkspaceContext workspace)
    {
        var rules = new List<(string Id, string Name, string Severity, bool Enabled)>();
        try
        {
            var wsNameLower = workspace.WorkspaceName.ToLowerInvariant();

            var kql =
                "Resources" +
                " | where type =~ \"microsoft.securityinsights/alertrules\"" +
                " | where subscriptionId =~ \"" + workspace.SubscriptionId + "\"" +
                " | where tolower(id) contains \"/workspaces/" + wsNameLower + "/providers/microsoft.securityinsights/\"" +
                " | project id," +
                "           displayName = tostring(properties.displayName)," +
                "           severity    = tostring(properties.severity)," +
                "           enabled     = tobool(properties.enabled)";

            var items = await QueryAsync(kql, new[] { workspace.SubscriptionId });

            foreach (var item in items)
            {
                rules.Add((
                    Id:       item.TryGetProperty("id",          out var id)  ? id.GetString()  ?? ""        : "",
                    Name:     item.TryGetProperty("displayName", out var dn)  ? dn.GetString()  ?? "Unknown" : "Unknown",
                    Severity: item.TryGetProperty("severity",    out var sev) ? sev.GetString() ?? "Unknown" : "Unknown",
                    Enabled:  item.TryGetProperty("enabled",     out var en)  && en.ValueKind == JsonValueKind.True
                ));
            }

            _logger.LogInformation("ARG returned {Count} analytics rules for workspace {Ws}",
                rules.Count, workspace.WorkspaceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ARG analytics rules query failed for workspace {Ws}", workspace.WorkspaceName);
        }

        return rules;
    }

    /// <summary>
    /// Returns DCR config (id, name, location, provisioningState, dataSources, destinations, dataFlows)
    /// for a resource group via ARG. Returns raw JsonElements for flexible downstream parsing.
    /// Replaces Azure.ResourceManager.Monitor SDK enumeration.
    /// </summary>
    public async Task<List<JsonElement>> GetDcrConfigAsync(WorkspaceContext workspace)
    {
        var results = new List<JsonElement>();
        try
        {
            var rgLower = workspace.ResourceGroup.ToLowerInvariant();

            var kql =
                "Resources" +
                " | where type =~ \"microsoft.insights/datacollectionrules\"" +
                " | where tolower(resourceGroup) =~ \"" + rgLower + "\"" +
                " | project id, name, location," +
                "           provisioningState = tostring(properties.provisioningState)," +
                "           dataSources       = properties.dataSources," +
                "           destinations      = properties.destinations," +
                "           dataFlows         = properties.dataFlows";

            results = await QueryAsync(kql, new[] { workspace.SubscriptionId });
            _logger.LogInformation("ARG returned {Count} DCRs in resource group {Rg}",
                results.Count, workspace.ResourceGroup);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ARG DCR query failed");
        }

        return results;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildRequestBody(string kql, IList<string> subscriptionIds, string? skipToken)
    {
        using var ms     = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteString("query", kql);

        writer.WriteStartArray("subscriptions");
        foreach (var id in subscriptionIds)
            writer.WriteStringValue(id);
        writer.WriteEndArray();

        writer.WriteStartObject("options");
        writer.WriteString("resultFormat", "objectArray");
        writer.WriteNumber("$top", 1000);
        if (skipToken != null)
            writer.WriteString("$skipToken", skipToken);
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
