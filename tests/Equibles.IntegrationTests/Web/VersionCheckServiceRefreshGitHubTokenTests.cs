using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Equibles.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: when `GitHubToken` is configured, the background refresh's outgoing
/// request to api.github.com must carry `Authorization: Bearer <token>`. The
/// token exists to lift the anonymous-IP rate limit (60/h → 5000/h); a refactor
/// that quietly drops or mis-schemes the header re-introduces the original
/// problem under a name nobody is looking at, and the failure mode (banner
/// silently stops updating) is the kind of bug that lives in production for
/// months before anyone notices.
/// </summary>
public class VersionCheckServiceRefreshGitHubTokenTests
{
    private class HeaderCapturingHandler : HttpMessageHandler
    {
        private readonly string _json;
        public AuthenticationHeaderValue LastAuthorization { get; private set; }
        public int Calls { get; private set; }

        public HeaderCapturingHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Calls++;
            LastAuthorization = request.Headers.Authorization;
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_json, Encoding.UTF8, "application/json"),
                }
            );
        }
    }

    [Fact]
    public async Task Refresh_GitHubTokenConfigured_AttachesBearerAuthorizationHeader()
    {
        const string token = "ghp_TEST_TOKEN_VALUE_0123456789";

        var handler = new HeaderCapturingHandler("{\"tag_name\":\"v1.1.1\"}");
        var factory = Substitute.For<IHttpClientFactory>();
        factory
            .CreateClient(Arg.Any<string>())
            .Returns(_ => new HttpClient(handler, disposeHandler: false));

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string>
                {
                    ["CheckForUpdates"] = "true",
                    ["GitHubToken"] = token,
                }
            )
            .Build();

        var sut = new VersionCheckService(
            factory,
            configuration,
            Substitute.For<ILogger<VersionCheckService>>()
        );

        sut.Get();

        for (var i = 0; i < 100 && handler.Calls == 0; i++)
        {
            await Task.Delay(20);
        }

        handler.Calls.Should().BeGreaterThan(0, "the background refresh must hit the stub");
        handler.LastAuthorization.Should().NotBeNull();
        handler.LastAuthorization!.Scheme.Should().Be("Bearer");
        handler.LastAuthorization.Parameter.Should().Be(token);
    }
}
