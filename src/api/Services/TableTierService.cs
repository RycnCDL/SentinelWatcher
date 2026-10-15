using System.Collections.Concurrent;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

/// <summary>Storage tier of a workspace table, as sent to the client.</summary>
public static class TableTiers
{
    public const string Analytics = "Analytics";
    public const string Basic = "Basic";
    public const string DataLake = "DataLake";
    public const string Unknown = "Unknown";

    /// <summary>
    /// Maps the Log Analytics table plan to a tier. Tables in the Microsoft Sentinel data lake
    /// tier have the Auxiliary plan in the Log Analytics tables API.
    /// </summary>
    public static string FromPlan(string? plan) => plan?.ToLowerInvariant() switch
    {
        "analytics" => Analytics,
        "basic" => Basic,
        "auxiliary" => DataLake,
        _ => Unknown,
    };

    /// <summary>
    /// Data lake tier tables can't be read through the Log Analytics query API, so a missing
    /// last log time means "not available", not "no data".
    /// </summary>
    public static bool IsDataLake(IReadOnlyDictionary<string, string> tiers, string table) =>
        tiers.TryGetValue(table, out var tier) && tier == DataLake;

    public static string Of(IReadOnlyDictionary<string, string> tiers, string table) =>
        tiers.TryGetValue(table, out var tier) ? tier : Unknown;
}

/// <summary>
/// Reads the plan of every table in a workspace from the Log Analytics tables API
/// (Microsoft.OperationalInsights/workspaces/tables/read) and caches it per workspace.
/// </summary>
public class TableTierService
{
    private const string ApiVersion = "2023-09-01";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(2);

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly ArmRestClient _arm;
    private readonly ILogger<TableTierService> _logger;
    private readonly ConcurrentDictionary<string, (IReadOnlyDictionary<string, string> Tiers, DateTimeOffset ExpiresAt)> _cache = new();

    public TableTierService(ArmRestClient arm, ILogger<TableTierService> logger)
    {
        _arm = arm;
        _logger = logger;
    }

    /// <summary>
    /// Table name to tier (see <see cref="TableTiers"/>). Empty when the tables API can't be read,
    /// in which case every table is treated as queryable, as before.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetTiersAsync(
        WorkspaceContext workspace, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(workspace.Key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Tiers;

        try
        {
            var items = await _arm.GetAllPagesAsync($"{workspace.ResourceId}/tables", ApiVersion, cancellationToken);
            var tiers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;

                var plan = item.TryGetProperty("properties", out var props) && props.TryGetProperty("plan", out var p)
                    ? p.GetString()
                    : null;
                tiers[name] = TableTiers.FromPlan(plan);
            }

            _logger.LogInformation("Read the plan of {Count} tables ({DataLake} in the data lake tier) for workspace {Workspace}",
                tiers.Count, tiers.Values.Count(t => t == TableTiers.DataLake), workspace.WorkspaceName);
            _cache[workspace.Key] = (tiers, DateTimeOffset.UtcNow + CacheDuration);
            return tiers;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read table plans for workspace {Workspace}", workspace.WorkspaceName);
            _cache[workspace.Key] = (Empty, DateTimeOffset.UtcNow + FailureCacheDuration);
            return Empty;
        }
    }
}
