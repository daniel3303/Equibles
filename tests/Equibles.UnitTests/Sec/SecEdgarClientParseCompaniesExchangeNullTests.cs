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
}
