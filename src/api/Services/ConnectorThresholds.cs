using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;

namespace SentinelWatcher.Api.Services;

/// <summary>
/// Gap thresholds per connector type: built-in defaults, overridden by ConnectorHealth settings.
/// </summary>
public sealed class ConnectorThresholds
{
    private const int OneDay = 24 * 60;

    /// <summary>
    /// Defaults for connectors whose data arrives in bursts, late, or only when events happen.
    /// Keys are connector types as shown on the Data Connectors page.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, ConnectorThresholdOverride> BuiltInDefaults =
        new Dictionary<string, ConnectorThresholdOverride>(StringComparer.OrdinalIgnoreCase)
        {
            // Event-driven: only sends data when something happens in the source.
            ["AzureActivity"]               = EventDriven(),
            ["ThreatIntelligence"]          = EventDriven(),
            ["ThreatIntelligenceTaxii"]     = EventDriven(),
            ["MicrosoftThreatIntelligence"] = EventDriven(),
            ["AzureSecurityCenter"]         = EventDriven(),
            ["AzureADIdentityProtection"]   = EventDriven(),
            ["MicrosoftCloudAppSecurity"]   = EventDriven(),
            ["OfficeIRM"]                   = EventDriven(),
            ["Sentinel"]                    = EventDriven(),

            // Continuous, but delivered with a known delay.
            ["AzureActiveDirectory"] = new() { WarningAfterMinutes = 120, ErrorAfterMinutes = 360 },
            ["Office365"]            = new() { WarningAfterMinutes = 240, ErrorAfterMinutes = 720 },
        };

    private readonly ConnectorHealthOptions _options;

    public ConnectorThresholds(IOptions<ConnectorHealthOptions> options)
    {
        _options = options.Value;
    }

    public ConnectorThreshold For(string connectorType)
    {
        var warning = Positive(_options.WarningAfterMinutes, 60);
        var error = Positive(_options.ErrorAfterMinutes, 120);
        var eventDriven = false;

        foreach (var source in new[] { BuiltInDefaults, _options.Connectors })
        {
            if (!source.TryGetValue(connectorType, out var o)) continue;
            if (o.WarningAfterMinutes is > 0) warning = o.WarningAfterMinutes.Value;
            if (o.ErrorAfterMinutes is > 0) error = o.ErrorAfterMinutes.Value;
            if (o.EventDriven.HasValue) eventDriven = o.EventDriven.Value;
        }

        return new ConnectorThreshold(warning, Math.Max(error, warning), eventDriven);
    }

    private static ConnectorThresholdOverride EventDriven() =>
        new() { WarningAfterMinutes = OneDay, ErrorAfterMinutes = OneDay, EventDriven = true };

    private static int Positive(int value, int fallback) => value > 0 ? value : fallback;
}
