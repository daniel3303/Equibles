using System.Collections;
using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientParseCompaniesFromResponseMissingColumnTests
{
    private static readonly MethodInfo ParseMethod = typeof(SecEdgarClient).GetMethod(
        "ParseCompaniesFromResponse",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void ParseCompaniesFromResponse_MissingTickerColumn_ReturnsEmpty()
    {
        // Contract: the SEC response must contain "cik", "name", and "ticker"
        // fields. If any expected column is absent (schema change), return an
        // empty list rather than blindly indexing into rows.
        var responseType = typeof(SecEdgarClient).Assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.CompanyTickersResponse"
        );
        var response = Activator.CreateInstance(responseType);
        responseType
            .GetProperty("Fields")!
            .SetValue(response, new List<string> { "cik", "name", "exchange" });
        responseType
            .GetProperty("Data")!
            .SetValue(
                response,
                new List<List<object>>
                {
                    new() { "0000320193", "Apple Inc", "Nasdaq" },
                }
            );

        var result = (IEnumerable)ParseMethod.Invoke(null, [response]);
        var companies = result.Cast<CompanyInfo>().ToList();

        companies.Should().BeEmpty();
    }
}
