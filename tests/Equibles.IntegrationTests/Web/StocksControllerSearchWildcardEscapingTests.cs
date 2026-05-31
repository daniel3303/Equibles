using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// End-to-end adversarial probe of the stock browser search box
/// (<c>GET /Stocks?search=...</c> → <c>StocksController.Index</c> →
/// <c>CommonStockRepository.Search</c>). The contract a search box promises is
/// "match rows whose ticker/name/description/industry CONTAINS the typed term";
/// the typed characters must be matched literally. <c>Search</c> splices the
/// term straight into <c>EF.Functions.ILike(col, $"%{word}%")</c> without
/// escaping LIKE metacharacters, so <c>_</c> (any single character) is honoured
/// as a wildcard: a search for the literal underscore matches every stock whose
/// columns hold at least one character. This test seeds two stocks that contain
/// no underscore anywhere and asserts neither is returned for <c>?search=_</c>.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksControllerSearchWildcardEscapingTests
{
    private readonly WebHostFixture _fixture;

    public StocksControllerSearchWildcardEscapingTests(WebHostFixture fixture) =>
        _fixture = fixture;

    [Fact(
        Skip = "GH-2907 — CommonStockRepository.Search treats LIKE wildcards _ and % as wildcards instead of literals"
    )]
    public async Task Index_UnderscoreSearchTerm_DoesNotMatchStocksWithoutLiteralUnderscore()
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

        var response = await _fixture.Client.GetAsync("/Stocks?search=_");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();

        // Neither seeded name contains a literal underscore, so a literal-term
        // search must return no rows. A wildcard-honouring '_' renders both.
        html.Should().NotContain("Apple Inc");
        html.Should().NotContain("Microsoft Corporation");
    }
}
