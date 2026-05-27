using System.Net;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;
using Equibles.Integrations.Common.RateLimiter;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Cboe;

public class CboeClientParsePutCallCsvVixColumnMappingTests
{
    // Contract (production comment at CboeClient.cs:102-103):
    //   "The VIX CSV ships columns in a different order than all other
    //   put/call CSVs: Date,Ratio,PutVol,CallVol,TotalVol vs
    //   Date,CallVol,PutVol,TotalVol,Ratio."
    //
    // ParsePutCallCsv branches on `isVix = csvType == CboePutCallCsvType.Vix`
    // and reads each column index from a different slot depending on the
    // branch:
    //   non-Vix: CallVolume=fields[1], PutVolume=fields[2],
    //            TotalVolume=fields[3], PutCallRatio=fields[4]
    //   isVix:   CallVolume=fields[3], PutVolume=fields[2],
    //            TotalVolume=fields[4], PutCallRatio=fields[1]
    //
    // The existing
    // DownloadPutCallRatios_EachCsvType_FetchesMatchingCdnFileAndReturnsParsedRecords
    // sibling pin covers Vix as a row in its [Theory] matrix, but it
    // (a) feeds the standard non-Vix CSV header into the Vix path and
    // (b) only asserts on the URL and the parsed Date — both of which
    // are isVix-branch-independent (Date is fields[0] in both layouts).
    // It does NOT exercise the isVix-specific column mapping; a refactor
    // that drops the isVix branch entirely (collapsing both branches into
    // a single non-Vix mapping under the false intuition that "all CBOE
    // CSVs look the same") would compile, pass that sibling, and
    // silently corrupt EVERY VIX put/call ratio record in production:
    //   • PutCallRatio (the only column non-Vix puts at fields[4]) would
    //     come from fields[4] which in the Vix layout is the TotalVol
    //     column — a ratio of "175000" instead of 0.75.
    //   • CallVolume (fields[3] in Vix, fields[1] in non-Vix) and
    //     TotalVolume (fields[4] in Vix, fields[3] in non-Vix) would
    //     each pick up the wrong neighbour column.
    //   • PutVolume (fields[2] in BOTH layouts) is the only one that
    //     would survive the regression.
    //
    // The production consequence: the put/call-ratio dashboard's VIX
    // panel renders nonsense values, the put/call sentiment indicator
    // used by the screener flips on a stale-but-wrong number, and the
    // CFTC sentiment cross-reference page silently disagrees with the
    // raw VIX feed because the wire ratio differs by ~5 orders of
    // magnitude.
    //
    // Pin: feed a CSV in the documented Vix layout (Date, Ratio, PutVol,
    // CallVol, TotalVol) with values chosen so EVERY column carries a
    // DISTINCT numeric value — no shared neighbours. That way an off-by-
    // one in either branch's index list surfaces as a mismatched value
    // on whichever field was swapped, not as a silent "looks plausible"
    // pass.
    //
    // Distinct values chosen so column swaps cannot pass by accident:
    //   Ratio    = 0.75      (decimal → only PutCallRatio assertion can accept)
    //   PutVol   = 12_345    (distinct long)
    //   CallVol  = 67_890    (distinct long)
    //   TotalVol = 80_235    (PutVol + CallVol, distinct long)
    //
    // Any swap between PutVolume↔CallVolume↔TotalVolume produces a wrong
    // assertion; any swap that puts PutCallRatio at the integer Ratio
    // slot stays decimal but the value mismatches.
    [Fact]
    public async Task DownloadPutCallRatios_VixCsvTypeWithVixLayout_MapsRatioPutCallTotalToCorrectColumns()
    {
        var vixLayoutCsv =
            "DATE,P/C RATIO,PUT VOLUME,CALL VOLUME,TOTAL VOLUME\n"
            + "01/15/2025,0.75,12345,67890,80235\n";
        var handler = new ScriptedHandler((HttpStatusCode.OK, vixLayoutCsv));
        var sut = new CboeClient(
            new HttpClient(handler),
            Substitute.For<ILogger<CboeClient>>(),
            Substitute.For<IRateLimiter>()
        );

        var result = await sut.DownloadPutCallRatios(CboePutCallCsvType.Vix);

        var record = result.Should().ContainSingle().Subject;
        record.Date.Should().Be(new DateOnly(2025, 1, 15));
        record.PutCallRatio.Should().Be(0.75m);
        record.PutVolume.Should().Be(12_345);
        record.CallVolume.Should().Be(67_890);
        record.TotalVolume.Should().Be(80_235);
    }

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses;

        public ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses)
        {
            _responses = new Queue<(HttpStatusCode, string)>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var (status, body) = _responses.Dequeue();
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body) }
            );
        }
    }
}
