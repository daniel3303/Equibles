using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models.Responses;
using Newtonsoft.Json;

namespace Equibles.UnitTests.Sec;

// ParseFundClassTickers reads SEC's company_tickers_mf.json — positional rows under a "fields"
// header, like the company file — into FundClassTicker rows. Pins the field-order independence
// (indices come from the header, not hard-coded positions), symbol normalization to uppercase,
// and that rows missing a series id or symbol are skipped rather than producing unusable entries.
public class SecEdgarClientParseFundClassTickersTests
{
    private static CompanyTickersResponse Response(string json) =>
        JsonConvert.DeserializeObject<CompanyTickersResponse>(json);

    [Fact]
    public void ParseFundClassTickers_ReadsRowsByHeaderPosition()
    {
        var response = Response(
            """
            {"fields":["cik","seriesId","classId","symbol"],
             "data":[[1174610,"S000014258","C000038817","usd"]]}
            """
        );

        var tickers = SecEdgarClient.ParseFundClassTickers(response);

        var ticker = tickers.Should().ContainSingle().Subject;
        ticker.Cik.Should().Be("1174610");
        ticker.SeriesId.Should().Be("S000014258");
        ticker.ClassId.Should().Be("C000038817");
        ticker.Symbol.Should().Be("USD", "symbols normalize to uppercase");
    }

    [Fact]
    public void ParseFundClassTickers_ReorderedFields_StillParses()
    {
        var response = Response(
            """
            {"fields":["symbol","seriesId","cik","classId"],
             "data":[["VOO","S000030356",36405,"C000093459"]]}
            """
        );

        var tickers = SecEdgarClient.ParseFundClassTickers(response);

        tickers.Should().ContainSingle().Which.SeriesId.Should().Be("S000030356");
    }

    [Fact]
    public void ParseFundClassTickers_RowsMissingSeriesOrSymbol_AreSkipped()
    {
        var response = Response(
            """
            {"fields":["cik","seriesId","classId","symbol"],
             "data":[[1,"","C1","ABC"],[2,"S000000002","C2",""],[3,"S000000003","C3","DEF"]]}
            """
        );

        var tickers = SecEdgarClient.ParseFundClassTickers(response);

        tickers.Should().ContainSingle().Which.Symbol.Should().Be("DEF");
    }

    [Fact]
    public void ParseFundClassTickers_MissingHeaderOrData_YieldsNothing()
    {
        SecEdgarClient.ParseFundClassTickers(null).Should().BeEmpty();
        SecEdgarClient
            .ParseFundClassTickers(Response("""{"fields":["cik"],"data":[[1]]}"""))
            .Should()
            .BeEmpty();
    }
}
