using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientGetFilingArtifactNamesEmptyCikTests
{
    // GetFilingArtifactNames documents "cik and accessionNumber are required"
    // as the synchronous precondition before any HTTP traffic. ASP.NET model
    // binding turns missing route/query parameters into empty strings rather
    // than null, so the guard must catch IsNullOrEmpty — not just null —
    // otherwise the scraper would issue requests against a malformed URL
    // (https://www.sec.gov/Archives/edgar/data//ACC-X/index.json) and waste
    // its strict SEC rate-limit budget on 404s. A refactor that swapped
    // `IsNullOrEmpty` for a `?? throw` or a null-only `ArgumentNullException`
    // would silently let "" through.
    [Fact]
    public async Task GetFilingArtifactNames_EmptyCik_ThrowsBeforeAnyHttpCall()
    {
        var client = new SecEdgarClient(
            new HttpClient(),
            NullLogger<SecEdgarClient>.Instance,
            new ConfigurationBuilder().Build()
        );

        var act = async () =>
            await client.GetFilingArtifactNames(
                cik: string.Empty,
                accessionNumber: "0001234567-24-000001"
            );

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*required*");
    }
}
