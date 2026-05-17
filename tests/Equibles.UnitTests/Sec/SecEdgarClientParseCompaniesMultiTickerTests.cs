using System.Collections;
using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to <see cref="SecEdgarClientParseCompaniesFromResponseTests"/>,
/// which only covers the skip guards. The documented grouping contract — "the
/// SEC file has one row per ticker … companies with multiple tickers appear as
/// multiple rows. The first ticker per CIK is the primary" — is unexercised.
/// A regression that emitted one CompanyInfo per row would create duplicate
/// stocks for every dual-class issuer (GOOGL/GOOG, BRK.A/BRK.B) downstream.
/// </summary>
public class SecEdgarClientParseCompaniesMultiTickerTests
{
    private static readonly MethodInfo ParseCompaniesFromResponseMethod =
        typeof(SecEdgarClient).GetMethod(
            "ParseCompaniesFromResponse",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void ParseCompaniesFromResponse_SameCikMultipleTickerRows_CollapsedToOneCompanyFirstTickerPrimary()
    {
        var responseType = typeof(SecEdgarClient).Assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.CompanyTickersResponse"
        );
        var response = Activator.CreateInstance(responseType);
        responseType
            .GetProperty("Fields")!
            .SetValue(response, new List<string> { "cik", "name", "ticker", "exchange" });
        responseType
            .GetProperty("Data")!
            .SetValue(
                response,
                new List<List<object>>
                {
                    new() { "0001652044", "Alphabet Inc.", "GOOGL", "Nasdaq" },
                    new() { "0001652044", "Alphabet Inc.", "GOOG", "Nasdaq" },
                    new() { "0000789019", "Microsoft Corp", "MSFT", "Nasdaq" },
                }
            );

        var result = (IEnumerable)ParseCompaniesFromResponseMethod.Invoke(null, [response]);
        var companies = result.Cast<CompanyInfo>().ToList();

        companies.Should().HaveCount(2, "the two GOOGL/GOOG rows are one company");
        var alphabet = companies.Single(c => c.Cik == "0001652044");
        // Order preserved: the first row's ticker is the primary.
        alphabet.Tickers.Should().Equal("GOOGL", "GOOG");
    }
}
