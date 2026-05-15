using System.Collections;
using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins <see cref="SecEdgarClient"/>'s private static ParseCompaniesFromResponse
/// malformed-row resilience. The two skip guards (short row, empty cik/ticker)
/// are zero-hit by every existing test — yet a regression removing the short-row
/// guard throws IndexOutOfRange and aborts the entire SEC company sync, and
/// removing the empty-cik/ticker guard poisons the CIK-keyed dictionary with
/// blank-key "companies". Same reflection pattern as the sibling SecEdgarClient
/// pins (the internal CompanyTickersResponse has no InternalsVisibleTo).
/// </summary>
public class SecEdgarClientParseCompaniesFromResponseTests
{
    private static readonly MethodInfo ParseCompaniesFromResponseMethod =
        typeof(SecEdgarClient).GetMethod(
            "ParseCompaniesFromResponse",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact]
    public void ParseCompaniesFromResponse_RowsTooShortOrMissingCikTicker_AreSkippedNotThrownOrEmitted()
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
                    new() { "0000320193" }, // too short — must skip, not IndexOutOfRange
                    new() { "", "Empty Co", "EMP", "Nasdaq" }, // empty cik — must skip
                    new() { "0000789019", "Microsoft Corp", "MSFT", "Nasdaq" }, // valid
                }
            );

        var result = (IEnumerable)ParseCompaniesFromResponseMethod.Invoke(null, [response]);
        var companies = result.Cast<CompanyInfo>().ToList();

        companies.Should().ContainSingle();
        companies[0].Cik.Should().Be("0000789019");
        companies[0].Tickers.Should().ContainSingle().Which.Should().Be("MSFT");
    }
}
