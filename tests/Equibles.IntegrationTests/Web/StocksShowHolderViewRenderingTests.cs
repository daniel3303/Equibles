using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// <c>StocksControllerShowHolderNotFoundTests</c> pins only the 404 guard via a
/// directly-instantiated controller, so <c>Views/Stocks/ShowHolder.cshtml</c>
/// is never rendered — 0% on its populated path. Pins the happy render: a
/// resolvable ticker + holder CIK returns 200 with the holder header and the
/// no-holdings empty-state branch (`Model.Holdings.Count == 0`).
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksShowHolderViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public StocksShowHolderViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetShowHolder_ResolvableTickerAndCik_RendersHolderHeaderAndEmptyState()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
            db.Add(new InstitutionalHolder { Cik = "0001067983", Name = "BERKSHIRE HATHAWAY INC" });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/Holders/0001067983");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("BERKSHIRE HATHAWAY INC", "the holder name header must render");
        html.Should()
            .Contain(
                "No Holdings History",
                "with no seeded holdings the empty-state branch must render"
            );
        html.Should()
            .Contain(
                "No holdings records found for this institution in AAPL",
                "the empty-state must interpolate the stock ticker"
            );
    }
}
