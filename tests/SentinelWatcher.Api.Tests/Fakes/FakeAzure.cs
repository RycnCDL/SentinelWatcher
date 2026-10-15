using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text;
using Azure;
using Azure.Core;
using Azure.Monitor.Query.Logs;
using Azure.Monitor.Query.Logs.Models;
using Microsoft.Extensions.Logging;

namespace SentinelWatcher.Api.Tests.Fakes;

/// <summary>
/// Log Analytics client that answers queries from a handler and records every call. No network.
/// </summary>
public sealed class FakeLogsQueryClient : LogsQueryClient
{
    private readonly Func<string, LogsQueryResult> _handler;

    public FakeLogsQueryClient(Func<string, LogsQueryResult> handler)
    {
        _handler = handler;
    }

    public ConcurrentQueue<(string Query, LogsQueryOptions? Options)> Calls { get; } = new();

    public int CountCalls(string queryFragment) =>
        Calls.Count(c => c.Query.Contains(queryFragment, StringComparison.Ordinal));

    public override async Task<Response<LogsQueryResult>> QueryResourceAsync(
        ResourceIdentifier resourceId, string query, LogsQueryTimeRange timeRange,
        LogsQueryOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Enqueue((query, options));
        await Task.Delay(20, cancellationToken); // lets concurrent callers overlap
        return Response.FromValue(_handler(query), new FakeResponse());
    }
}

/// <summary>Builds query results with the SDK model factory.</summary>
public static class LogsResults
{
    public static LogsQueryResult Empty() => Table(Array.Empty<(string, LogsColumnType)>());

    /// <summary>A result with one table. A non-null error makes it a partial failure.</summary>
    public static LogsQueryResult Table(
        (string Name, LogsColumnType Type)[] columns, IEnumerable<object?[]>? rows = null, string? partialError = null)
    {
        var cols = columns.Select(c => LogsQueryModelFactory.LogsTableColumn(c.Name, c.Type)).ToList();
        var tableRows = (rows ?? Array.Empty<object?[]>()).Select(r => LogsQueryModelFactory.LogsTableRow(cols, r)).ToList();
        var table = LogsQueryModelFactory.LogsTable("PrimaryResult", cols, tableRows);
        var error = partialError is null ? null : BinaryData.FromString(partialError);
        // Named arguments: the parameter order differs between the XML docs and the assembly.
        return LogsQueryModelFactory.LogsQueryResult(
            allTables: new[] { table },
            error: error,
            statistics: BinaryData.FromString("{}"),
            visualization: BinaryData.FromString("{}"));
    }

    /// <summary>The error Log Analytics returns when a union isfuzzy=true skips a missing table.</summary>
    public static string MissingTableError(string table) => $$"""
        {
          "code": "PartialError",
          "message": "There were some errors when processing your query.",
          "details": [{
            "code": "EngineError",
            "message": "'union' operator: Failed to resolve table expression named '{{table}}'",
            "innererror": {
              "code": "SEM0100",
              "message": "'union' operator: Failed to resolve table expression named '{{table}}'"
            }
          }]
        }
        """;
}

/// <summary>Answers ARM GET requests by path and records them.</summary>
public sealed class FakeArmHandler : HttpMessageHandler
{
    private readonly Func<string, string?> _responder;

    public FakeArmHandler(Func<string, string?> responder)
    {
        _responder = responder;
    }

    public ConcurrentQueue<string> Paths { get; } = new();

    public int CountCalls(string pathSuffix) =>
        Paths.Count(p => p.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        Paths.Enqueue(path);
        await Task.Delay(20, cancellationToken);
        var body = _responder(path);
        return body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

public sealed class FakeTokenCredential : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new("fake-token", DateTimeOffset.UtcNow.AddHours(1));

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
        new(GetToken(requestContext, cancellationToken));
}

/// <summary>Collects log entries so tests can check what was logged and how often.</summary>
public sealed class ListLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(LogLevel Level, string Category, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new ListLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class ListLogger(ListLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner.Entries.Enqueue((logLevel, category,
                exception is null ? formatter(state, exception) : $"{formatter(state, exception)} {exception}"));
    }
}

internal sealed class FakeResponse : Response
{
    public override int Status => 200;
    public override string ReasonPhrase => "OK";
    public override Stream? ContentStream { get; set; }
    public override string ClientRequestId { get; set; } = string.Empty;

    public override void Dispose()
    {
    }

    protected override bool ContainsHeader(string name) => false;

    protected override IEnumerable<HttpHeader> EnumerateHeaders() => Array.Empty<HttpHeader>();

    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = null;
        return false;
    }

    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        values = null;
        return false;
    }
}
