using Azure.Monitor.Query.Logs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;
using SentinelWatcher.Api.Tests.Fakes;

namespace SentinelWatcher.Api.Tests;

public class PartialQueryErrorTests
{
    private static readonly (string, LogsColumnType)[] LastLogColumns =
    {
        ("Type", LogsColumnType.String),
        ("LastLog", LogsColumnType.Datetime),
    };

    [Fact]
    public async Task LastLogUnion_UsesRowsOfAPartialResult_WithoutFallbackQueries()
    {
        var lastSyslog = DateTimeOffset.UtcNow.AddMinutes(-5);
        var client = new FakeLogsQueryClient(query => query.Contains("union isfuzzy=true")
            ? LogsResults.Table(LastLogColumns,
                new[] { new object?[] { "Syslog", lastSyslog.ToString("o") } },
                LogsResults.MissingTableError("InsiderRiskEvents"))
            : LogsResults.Empty());
        var (service, logs) = CreateService(client);

        var result = await service.GetLastLogPerTableAsync(WorkspaceRegistry.Parse(TestApp.AllowedWorkspace));

        Assert.True(result.ContainsKey("Syslog"), string.Join(Environment.NewLine, logs.Entries));
        Assert.Equal(lastSyslog.UtcDateTime, result["Syslog"]!.Value, TimeSpan.FromSeconds(1));

        var union = Assert.Single(client.Calls, c => c.Query.Contains("union isfuzzy=true"));
        Assert.True(union.Options?.AllowPartialErrors);

        // The old code fell back to one query per table, which produced 400 responses for
        // tables that don't exist. Only the union (and the Usage freshness query) may run.
        Assert.All(client.Calls, c => Assert.True(
            c.Query.Contains("union isfuzzy=true") || c.Query.Contains("Usage"),
            $"Unexpected query: {c.Query.Trim()}"));
    }

    [Fact]
    public async Task MissingTables_AreLoggedOnceAtInformationLevel()
    {
        var client = new FakeLogsQueryClient(query => query.Contains("union isfuzzy=true")
            ? LogsResults.Table(LastLogColumns, partialError: LogsResults.MissingTableError("InsiderRiskEvents"))
            : LogsResults.Empty());
        var (service, logs) = CreateService(client);
        var workspace = WorkspaceRegistry.Parse(TestApp.AllowedWorkspace);

        await service.GetLastLogPerTableAsync(workspace);
        await service.GetLastLogPerTableAsync(workspace);

        Assert.True(logs.Entries.Any(e => e.Message.Contains("InsiderRiskEvents")), string.Join(Environment.NewLine, logs.Entries));
        var entry = Assert.Single(logs.Entries, e => e.Message.Contains("InsiderRiskEvents"));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.True(!logs.Entries.Any(e => e.Level >= LogLevel.Warning), string.Join(Environment.NewLine, logs.Entries));
    }

    private static (LogAnalyticsService Service, ListLoggerProvider Logs) CreateService(FakeLogsQueryClient client)
    {
        var logs = new ListLoggerProvider();
        var factory = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        var service = new LogAnalyticsService(
            client, Options.Create(new CacheSettingsConfig()), factory.CreateLogger<LogAnalyticsService>());
        return (service, logs);
    }
}
