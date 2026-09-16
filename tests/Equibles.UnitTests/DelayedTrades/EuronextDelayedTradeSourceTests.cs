using System.Net;
using System.Security.Cryptography;
using Equibles.Integrations.DelayedTrades;
using Equibles.Integrations.Euronext.DelayedTrades;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: the source composes the venue's file URL from the location and window, treats a 404 as
/// "no session" rather than a fault, retries a throttle or server error, refuses a redirect off the
/// official origin, and records the zip's hash and size on the served file.
/// </summary>
public class EuronextDelayedTradeSourceTests
{
    private static readonly byte[] Zip = TradesFixture.Zip(
        TradesFixture.Csv(TradesFixture.HeaderOnly)
    );

    private static (EuronextDelayedTradeSource Source, DelayedTradeTestHandler Handler) Build(
        params (HttpStatusCode, byte[], Uri)[] responses
    )
    {
        var handler = new DelayedTradeTestHandler(responses);
        return (new EuronextDelayedTradeSource(new HttpClient(handler)), handler);
    }

    [Theory]
    [InlineData(DelayedTradeWindow.CurrentSession, "CURRENT_TRADING_DAY")]
    [InlineData(DelayedTradeWindow.PreviousSession, "PREVIOUS_TRADING_DAY")]
    public void FileUrl_NamesTheWindowAndLocation(DelayedTradeWindow window, string segment)
    {
        EuronextDelayedTradeSource
            .FileUrl("LIS", window)
            .ToString()
            .Should()
            .Be(
                $"https://marketdata.euronext.com/data-reporting-service/trades-file/download/EQUITIES/{segment}/LIS"
            );
    }

    [Theory]
    [InlineData("")]
    [InlineData("lis")]
    [InlineData("LIS/../PAR")]
    public void FileUrl_RefusesAnythingButAShortUpperCaseLocation(string location)
    {
        var act = () =>
            EuronextDelayedTradeSource.FileUrl(location, DelayedTradeWindow.CurrentSession);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task AServedZip_CarriesItsHashSizeAndTerms()
    {
        var (source, handler) = Build((HttpStatusCode.OK, Zip, null));

        var file = await source.Fetch(
            "LIS",
            DelayedTradeWindow.PreviousSession,
            CancellationToken.None
        );

        file.Outcome.Should().Be(DelayedTradeFetchOutcome.Served);
        file.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Zip)));
        file.Bytes.Should().Be(Zip.Length);
        file.Content.Should().Equal(Zip);
        file.TermsUrl.Should().Be(EuronextDelayedTradeTerms.TermsUrl);
        file.SourceUrl.Should().Be(handler.Requests.Single().ToString());
        file.FetchedAtUtc.Kind.Should().Be(DateTimeKind.Utc);
        source.Parse(file, new DelayedTradeParseCounters()).Should().BeEmpty();
    }

    [Fact]
    public async Task ANotFound_IsNoSession_NotAFault()
    {
        var (source, _) = Build((HttpStatusCode.NotFound, [], null));

        var file = await source.Fetch(
            "LIS",
            DelayedTradeWindow.CurrentSession,
            CancellationToken.None
        );

        file.Outcome.Should().Be(DelayedTradeFetchOutcome.NoSession);
        file.Content.Should().BeNull();
        file.Sha256.Should().BeNull();
        source.Parse(file, new DelayedTradeParseCounters()).Should().BeEmpty();
    }

    [Fact]
    public async Task AServerError_IsRetriedThenServed()
    {
        var (source, handler) = Build(
            (HttpStatusCode.ServiceUnavailable, [], null),
            (HttpStatusCode.OK, Zip, null)
        );

        var file = await source.Fetch(
            "LIS",
            DelayedTradeWindow.PreviousSession,
            CancellationToken.None
        );

        file.Outcome.Should().Be(DelayedTradeFetchOutcome.Served);
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ARedirectOffTheOrigin_IsRefused()
    {
        var (source, _) = Build(
            (HttpStatusCode.OK, Zip, new Uri("https://cdn.example.com/trades.zip"))
        );

        var act = () =>
            source.Fetch("LIS", DelayedTradeWindow.PreviousSession, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*origin*");
    }

    [Fact]
    public async Task AClientError_Throws()
    {
        var (source, _) = Build((HttpStatusCode.Forbidden, [], null));

        var act = () =>
            source.Fetch("LIS", DelayedTradeWindow.PreviousSession, CancellationToken.None);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public void Attribution_NamesTheVenueAndItsTerms()
    {
        var source = new EuronextDelayedTradeSource(new HttpClient());
        source.SourceKey.Should().Be("euronext");
        source.Attribution.SourceName.Should().Be("Euronext");
        source
            .Attribution.TermsUrl.Should()
            .Be("https://www.euronext.com/delayed-data-terms-conditions");
    }
}
