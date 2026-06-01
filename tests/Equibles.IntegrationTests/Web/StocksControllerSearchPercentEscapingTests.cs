using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Twin of <see cref="StocksControllerSearchWildcardEscapingTests"/> for the other LIKE
/// metacharacter. A search box promises a literal "contains" match, so a typed '%' must be
/// matched literally rather than honoured as LIKE's "any run of characters" wildcard — which
/// would make '%' match every stock. Seeds two stocks containing no '%' anywhere and asserts
/// neither is returned for <c>?search=%</c>.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksControllerSearchPercentEscapingTests
{
    private readonly WebHostFixture _fixture;

    public StocksControllerSearchPercentEscapingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Index_PercentSearchTerm_DoesNotMatchStocksWithoutLiteralPercent()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Ticker = "AAPL",
                    Name = "Apple Inc",
                    Description = "Consumer electronics maker",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new CommonStock
                {
                    Ticker = "MSFT",
                    Name = "Microsoft Corporation",
                    Description = "Enterprise software vendor",
                    Cik = "0000789019",
                }
            );
            await db.SaveChangesAsync();
        });

        var response = await _fixture.Client.GetAsync("/Stocks?search=%25");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        // Neither seeded row contains a literal '%', so a literal-term search must return
        // no rows. A wildcard-honouring '%' would render both.
        html.Should().NotContain("Apple Inc");
        html.Should().NotContain("Microsoft Corporation");
    }
}
