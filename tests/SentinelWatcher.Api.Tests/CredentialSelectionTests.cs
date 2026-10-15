using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using SentinelWatcher.Api.Models;
using SentinelWatcher.Api.Services;
using SentinelWatcher.Api.Tests.Fakes;

namespace SentinelWatcher.Api.Tests;

public class CredentialSelectionTests
{
    private static readonly TokenRequestContext Arm = new(new[] { "https://management.azure.com/.default" });

    private static (ILogger Logger, ListLoggerProvider Logs) Logger()
    {
        var logs = new ListLoggerProvider();
        var factory = LoggerFactory.Create(b => b.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        return (factory.CreateLogger("test"), logs);
    }

    [Fact]
    public void WithoutManagedIdentityClientId_UsesDeveloperChainStartingWithAzureCli()
    {
        var (logger, logs) = Logger();

        var credential = AzureCredentialFactory.Create(new SentinelOptions { ManagedIdentityClientId = "" }, logger);

        var chain = Assert.IsType<DeveloperCredentialChain>(credential);
        Assert.IsType<AzureCliCredential>(chain.Sources[0]);
        Assert.Equal(
            new[] { typeof(AzureCliCredential), typeof(VisualStudioCredential), typeof(VisualStudioCodeCredential), typeof(AzurePowerShellCredential) },
            chain.Sources.Select(s => s.GetType()));
        Assert.DoesNotContain(chain.Sources, s =>
            s is ManagedIdentityCredential or WorkloadIdentityCredential or DefaultAzureCredential or EnvironmentCredential);

        var entry = Assert.Single(logs.Entries, e => e.Message.StartsWith("Azure credential:"));
        Assert.Contains("AzureCliCredential", entry.Message);
    }

    [Fact]
    public void WithManagedIdentityClientId_UsesTheManagedIdentity()
    {
        var (logger, _) = Logger();

        var credential = AzureCredentialFactory.Create(
            new SentinelOptions { ManagedIdentityClientId = "33333333-3333-3333-3333-333333333333" }, logger);

        Assert.IsType<ManagedIdentityCredential>(credential);
    }

    [Fact]
    public async Task Chain_SkipsUnavailableSources_AndLogsTheSourceOnce()
    {
        var (logger, logs) = Logger();
        var chain = new DeveloperCredentialChain(new TokenCredential[]
        {
            new StubCredential(new CredentialUnavailableException("az not signed in")),
            new FakeTokenCredential(),
        }, logger);

        var first = await chain.GetTokenAsync(Arm, default);
        await chain.GetTokenAsync(Arm, default);

        Assert.Equal("fake-token", first.Token);
        Assert.Single(logs.Entries, e => e.Message.Contains("signed in with FakeTokenCredential"));
    }

    [Fact]
    public async Task Chain_StopsOnAnAuthenticationError()
    {
        var (logger, _) = Logger();
        var second = new FakeTokenCredential();
        var chain = new DeveloperCredentialChain(new TokenCredential[]
        {
            new StubCredential(new AuthenticationFailedException("token expired")),
            second,
        }, logger);

        await Assert.ThrowsAsync<AuthenticationFailedException>(() => chain.GetTokenAsync(Arm, default).AsTask());
    }

    [Fact]
    public async Task Chain_WithNoSourceAvailable_SaysToRunAzLogin()
    {
        var (logger, _) = Logger();
        var chain = new DeveloperCredentialChain(new TokenCredential[]
        {
            new StubCredential(new CredentialUnavailableException("az not signed in")),
            new StubCredential(new CredentialUnavailableException("VS not installed")),
        }, logger);

        var ex = await Assert.ThrowsAsync<CredentialUnavailableException>(() => chain.GetTokenAsync(Arm, default).AsTask());
        Assert.Contains("az login", ex.Message);
    }

    private sealed class StubCredential(Exception error) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw error;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromException<AccessToken>(error);
    }
}
