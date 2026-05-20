using System.Net.Http;
using System.Reflection;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientTryParseTransactionLineNoOwnerCodeTests
{
    [Fact]
    public void TryParseTransactionLine_LineWithoutOwnerCodePrefix_ReturnsFalseWithoutBuildingTransaction()
    {
        // HouseDisclosureClient.TryParseTransactionLine (extracted in #1472)
        // is invoked once per PDF text line during the House PTR parsing
        // pass. The PDF's raw text iterator yields EVERY line — headers,
        // page numbers, footer boilerplate ("Filing ID 12345"), continued-
        // from-prev-page markers — interleaved with the real transaction
        // rows. Real transaction rows always start with the owner code
        // (SP/JT/DC/Self matched by OwnerCodeRegex); other lines must be
        // skipped silently.
        //
        // The risk this catches: a refactor that drops the
        //   `if (!ownerMatch.Success) return false;`
        // early-return (perhaps under the false intuition that "downstream
        // matchers will reject non-transaction lines anyway") would
        // compile, pass any test that feeds well-formed transaction
        // strings, and start treating every header/footer/page-number
        // line as a candidate transaction. Each member's filing would
        // explode into dozens of false-positive DisclosureTransaction
        // rows polluting the CongressionalTrade table — and once
        // persisted, every downstream screener and per-member view shows
        // garbage tickers (the assetName-extracted phrase from a
        // boilerplate line) attributed to that member.
        //
        // Pin: a line that does not start with an owner code (e.g. the
        // "Filing ID" footer) must return false. The transaction out-
        // parameter is `null` since the early-return fires before any
        // construction.
        var client = new HouseDisclosureClient(
            httpClient: new HttpClient(),
            logger: NullLogger<HouseDisclosureClient>.Instance
        );

        var method = typeof(HouseDisclosureClient).GetMethod(
            "TryParseTransactionLine",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var line = "Filing ID 12345 — page 1 of 3";
        object[] args = [line, null, null];
        var success = (bool)method.Invoke(client, args);

        success.Should().BeFalse();
        args[2].Should().BeNull();
    }
}
