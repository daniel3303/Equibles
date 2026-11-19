using System.Collections;
using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// EDGAR's company_tickers_exchange.json carries ticker rows with a null
/// exchange — private or pre-listing registrants (SpaceX is listed as "SPCX",
/// exchange null). Those symbols are recycled by real instruments on
/// exchanges, so creating a company from such a row lets the Yahoo enrichment
/// attach another instrument's prices and market cap (the $1.77T SpaceX
/// phantom, EquiblesCommercial#2515). Pin: exchange-null rows never produce a
/// company or a ticker; a company keeps only its exchange-listed rows.
/// </summary>
public class SecEdgarClientParseCompaniesExchangeNullTests
{
    private static readonly MethodInfo ParseCompaniesFromResponseMethod =
        typeof(SecEdgarClient).GetMethod(
            "ParseCompaniesFromResponse",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static List<CompanyInfo> Parse(List<List<object>> data)
    {
        var responseType = typeof(SecEdgarClient).Assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.CompanyTickersResponse"
        );
        var response = Activator.CreateInstance(responseType);
        responseType
            .GetProperty("Fields")!
            .SetValue(response, new List<string> { "cik", "name", "ticker", "exchange" });
        responseType.GetProperty("Data")!.SetValue(response, data);

        var result = (IEnumerable)ParseCompaniesFromResponseMethod.Invoke(null, [response]);
        return result.Cast<CompanyInfo>().ToList();
    }

    [Fact]
    public void ParseCompaniesFromResponse_ExchangeNullRows_NeverBecomeCompaniesOrTickers()
    {
        var companies = Parse([
            // Private registrant: every ticker row is exchange-null (SpaceX).
            new() { "0001181412", "SPACE EXPLORATION TECHNOLOGIES CORP", "SPCX", null },
            // Listed company.
            new() { "0000789019", "Microsoft Corp", "MSFT", "Nasdaq" },
            // Mixed: an exchange-null row precedes the real listing — the
            // company survives with only the exchange-listed ticker.
            new() { "0001362988", "Aircastle LTD", "AYR", null },
            new() { "0001362988", "Aircastle LTD", "AYR-PA", "NYSE" },
        ]);

        companies
            .Should()
            .NotContain(
                c => c.Cik == "0001181412",
                "an all-null-exchange registrant is not a listed company"
            );

        var mixed = companies.Single(c => c.Cik == "0001362988");
        mixed.Tickers.Should().Equal(["AYR-PA"], "only exchange-listed ticker rows survive");

        companies.Single(c => c.Cik == "0000789019").Tickers.Should().Equal(["MSFT"]);
    }

    private static readonly MethodInfo ParseUnlistedRegistrantsFromResponseMethod =
        typeof(SecEdgarClient).GetMethod(
            "ParseUnlistedRegistrantsFromResponse",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // Rows as SEC's directory stated them on 2026-11-19, Anthropic's first trading day.
    private static readonly List<List<object>> FirstTradingDayRows =
    [
        new() { 789019, "MICROSOFT CORP", "MSFT", "Nasdaq" },
        new() { 1041859, "Childrens Place, Inc.", "PLCEQ", null },
        new() { 2130357, "Anthropic, PBC", "ANT", null },
        new() { 2142627, "BHAV Acquisition Corp II", "BHAI", null },
        new() { 1041859, "Childrens Place, Inc.", "PLCE", "Nasdaq" },
        new() { 1496254, "Alternative Strategies Income Fund", "LTAFX", null },
        new() { 1496254, "Alternative Strategies Income Fund", "LTCFX", null },
        new() { 2142627, "BHAV Acquisition Corp II", "BHAIU", "Nasdaq" },
    ];

    // Only a registrant with no exchange-listed row at all is offered for admission on other
    // evidence; a blank row of a listed registrant (a SPAC's share symbol before it separates)
    // stays skipped, and the listed set is unchanged.
    [Fact]
    public void ParseUnlistedRegistrantsFromResponse_KeepsOnlyRegistrantsWithNoListedRow()
    {
        var response = Response(FirstTradingDayRows);

        var unlisted = (
            (IEnumerable)ParseUnlistedRegistrantsFromResponseMethod.Invoke(null, [response])
        )
            .Cast<CompanyInfo>()
            .ToDictionary(c => c.Cik, c => c.Tickers);

        unlisted
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, List<string>>
                {
                    ["2130357"] = ["ANT"],
                    ["1496254"] = ["LTAFX", "LTCFX"],
                }
            );
        Parse(FirstTradingDayRows)
            .ToDictionary(c => c.Cik, c => c.Tickers)
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, List<string>>
                {
                    ["789019"] = ["MSFT"],
                    ["1041859"] = ["PLCE"],
                    ["2142627"] = ["BHAIU"],
                }
            );
    }

    private static object Response(List<List<object>> data)
    {
        var responseType = typeof(SecEdgarClient).Assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.CompanyTickersResponse"
        );
        var response = Activator.CreateInstance(responseType);
        responseType
            .GetProperty("Fields")!
            .SetValue(response, new List<string> { "cik", "name", "ticker", "exchange" });
        responseType.GetProperty("Data")!.SetValue(response, data);
        return response;
    }

    // The directory's exchange is the dated statement the ticker ledger records, so every kept
    // ticker carries it verbatim, an over-the-counter line included.
    [Fact]
    public void ParseCompaniesFromResponse_ListedRows_KeepTheirStatedExchange()
    {
        var companies = Parse([
            new() { "0000312070", "BARCLAYS BANK PLC", "ATMP", "CBOE" },
            new() { "0000312070", "BARCLAYS BANK PLC", "BWVTF", "OTC" },
            new() { "0000312070", "BARCLAYS BANK PLC", "SPCX", null },
            new() { "0001077428", "TEXAS CAPITAL BANCSHARES INC/TX", "TCBI-PB", " NYSE " },
        ]);

        companies
            .Single(c => c.Cik == "0000312070")
            .TickerExchanges.Should()
            .BeEquivalentTo(
                new Dictionary<string, string> { ["ATMP"] = "CBOE", ["BWVTF"] = "OTC" }
            );
        companies
            .Single(c => c.Cik == "0001077428")
            .TickerExchanges.Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["TCBI-PB"] = "NYSE" });
    }
}
