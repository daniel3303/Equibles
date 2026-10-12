using System.Net;
using System.Net.Http.Headers;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// FilingExists answers only from the filing directory's status: a 404 is a removed filing and a
/// 200 a published one, whatever the listing holds; anything else throws, so an unreachable
/// directory is never read as a removed filing.
/// </summary>
public class SecEdgarClientFilingExistsTests
{
    private const string Cik = "1829805";
    private const string Accession = "0001829805-24-000006";

    private static SecEdgarClient BuildClient(HttpMessageHandler handler)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" }
            )
            .Build();
        return new SecEdgarClient(
            new HttpClient(handler),
            NullLogger<SecEdgarClient>.Instance,
            config
        );
    }

    [Fact]
    public async Task FilingExists_DirectoryNotFound_ReturnsFalse()
    {
        var handler = new StatusHandler(HttpStatusCode.NotFound);
        var sut = BuildClient(handler);

        var exists = await sut.FilingExists(Cik, Accession);

        exists.Should().BeFalse();
        handler.CallCount.Should().Be(1);
        handler
            .LastUrl.Should()
            .EndWith("/Archives/edgar/data/1829805/000182980524000006/index.json");
    }

    [Fact]
    public async Task FilingExists_DirectoryPublished_ReturnsTrueEvenWhenItemless()
    {
        var handler = new StatusHandler(HttpStatusCode.OK, "{\"directory\":{\"item\":[]}}");
        var sut = BuildClient(handler);

        var exists = await sut.FilingExists(Cik, Accession);

        exists.Should().BeTrue("only the status decides; an item-less listing is still published");
    }

    [Fact]
    public async Task FilingExists_UnrecognizedForbidden_Throws()
    {
        var handler = new StatusHandler(HttpStatusCode.Forbidden, "something unexpected");
        var sut = BuildClient(handler);

        var act = async () => await sut.FilingExists(Cik, Accession);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.CallCount.Should().Be(1);
    }

    private sealed class StatusHandler(HttpStatusCode status, string body = "") : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            LastUrl = request.RequestUri.ToString();
            var response = new HttpResponseMessage(status) { Content = new StringContent(body) };
            // Retry-After: 0 keeps any backoff path fast.
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        }
    }
}
