using System.Net;
using System.Text;
using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Fred;

/// <summary>
/// During FRED's 2026-10-21 outage every request answered 200 with an HTML page, and each
/// curated series failed JSON parsing with "Unexpected character encountered while parsing
/// value: &lt;" as its own Errors row. The client must name that page as an outage so the
/// importers can stop the cycle.
/// </summary>
public class FredClientOutagePageTests
{
    private const string OutagePage =
        "<!DOCTYPE html><html><head><title>FRED</title></head>"
        + "<body>FRED is temporarily unavailable.</body></html>";

    [Fact]
    public async Task GetObservations_OkWithHtmlPage_ThrowsFredUnavailable()
    {
        var sut = Client(new StringContent(OutagePage, Encoding.UTF8, "text/html"));

        var act = () => sut.GetObservations("ANFCI");

        (await act.Should().ThrowAsync<FredUnavailableException>())
            .Which.StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetReleaseDates_OkWithHtmlBodyAndNoHtmlContentType_ThrowsFredUnavailable()
    {
        // The recorded failure only tells us the body began with '<'; the content type is
        // unknown, so the body alone must be enough.
        var sut = Client(new StringContent("\n  " + OutagePage));

        var act = () => sut.GetReleaseDates(10, new DateOnly(2026, 10, 1));

        await act.Should().ThrowAsync<FredUnavailableException>();
    }

    private static FredClient Client(HttpContent content) =>
        new(
            new HttpClient(new FixedHandler(content)),
            Substitute.For<ILogger<FredClient>>(),
            Options.Create(new FredOptions { ApiKey = "test-key" })
        );

    private sealed class FixedHandler(HttpContent content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }
}
