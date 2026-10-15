using System.Net;

namespace SentinelWatcher.Api.Tests;

public class InputValidationTests : IClassFixture<TestApp>
{
    private readonly TestApp _app;

    public InputValidationTests(TestApp app)
    {
        _app = app;
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("31")]
    [InlineData("100000")]
    [InlineData("abc")]
    [InlineData("7d")]
    public async Task SentinelHealth_InvalidDays_Returns400(string days)
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);

        var response = await client.GetAsync(
            $"/api/sentinelhealth?workspace={Uri.EscapeDataString(TestApp.AllowedWorkspace)}&days={Uri.EscapeDataString(days)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiRoute_Returns404()
    {
        var client = _app.CreateSignedInClient(TestRoles.Viewer);

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
