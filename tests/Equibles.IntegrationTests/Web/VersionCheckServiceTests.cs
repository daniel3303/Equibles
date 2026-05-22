using System.Net;
using System.Text;
using Equibles.Web.Models;
using Equibles.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

public class VersionCheckServiceTests
{
    // Replays a canned GitHub "latest release" response without touching the network.
    private class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;
        private readonly HttpStatusCode _status;
        public int Calls { get; private set; }

        public StubHttpMessageHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _json = json;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            return Task.FromResult(
                new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    private static VersionCheckService CreateService(
        StubHttpMessageHandler handler,
        bool checkForUpdates = true
    )
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["CheckForUpdates"] = checkForUpdates ? "true" : "false",
                }
            )
            .Build();

        return new VersionCheckService(
            factory,
            configuration,
            Substitute.For<ILogger<VersionCheckService>>()
        );
    }

    // Polls Get() until the background refresh has populated the cache.
    private static async Task<VersionCheckResult> WaitForRefresh(VersionCheckService sut)
    {
        for (var i = 0; i < 100; i++)
        {
            var result = sut.Get();
            if (result.LatestVersion != null)
            {
                return result;
            }

            await Task.Delay(20);
        }

        return sut.Get();
    }

    [Fact]
    public void Get_CheckForUpdatesDisabled_ReturnsNoUpdateWithoutHttpCall()
    {
        var handler = new StubHttpMessageHandler("{}");
        var sut = CreateService(handler, checkForUpdates: false);

        var result = sut.Get();

        Assert.False(result.UpdateAvailable);
        Assert.False(string.IsNullOrEmpty(result.CurrentVersion));
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Get_ColdCache_ReturnsImmediatelyWithoutBlocking()
    {
        var handler = new StubHttpMessageHandler("{\"tag_name\":\"v99.0.0\"}");
        var sut = CreateService(handler);

        var result = sut.Get();

        // First call must not wait on the background refresh.
        Assert.False(result.UpdateAvailable);
        Assert.Null(result.LatestVersion);
    }

    [Fact]
    public async Task Get_NewerReleaseAvailable_ReportsUpdate()
    {
        var handler = new StubHttpMessageHandler(
            "{\"tag_name\":\"v99.0.0\",\"html_url\":\"https://github.com/daniel3303/Equibles/releases/tag/v99.0.0\"}"
        );
        var sut = CreateService(handler);

        var result = await WaitForRefresh(sut);

        Assert.True(result.UpdateAvailable);
        Assert.Equal("99.0.0", result.LatestVersion);
        Assert.Equal("1.1.0", result.CurrentVersion);
        Assert.Equal(
            "https://github.com/daniel3303/Equibles/releases/tag/v99.0.0",
            result.ReleaseUrl
        );
    }

    [Fact]
    public async Task Get_SameVersion_DoesNotReportUpdate()
    {
        var handler = new StubHttpMessageHandler("{\"tag_name\":\"v1.1.0\"}");
        var sut = CreateService(handler);

        var result = await WaitForRefresh(sut);

        Assert.False(result.UpdateAvailable);
        Assert.Equal("1.1.0", result.LatestVersion);
    }

    [Fact]
    public async Task Get_FourSegmentSameVersionTag_DoesNotReportSpuriousUpdate()
    {
        // Regression: "v1.1.0.0" must not compare as newer than assembly "1.1.0".
        var handler = new StubHttpMessageHandler("{\"tag_name\":\"v1.1.0.0\"}");
        var sut = CreateService(handler);

        var result = await WaitForRefresh(sut);

        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public void Get_GitHubUnreachable_FailsSilentWithNoUpdate()
    {
        var handler = new StubHttpMessageHandler("nope", HttpStatusCode.InternalServerError);
        var sut = CreateService(handler);

        // Cold call returns immediately; the failed background refresh must not throw.
        var result = sut.Get();

        Assert.False(result.UpdateAvailable);
    }
}
