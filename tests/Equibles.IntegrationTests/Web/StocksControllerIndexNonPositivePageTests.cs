using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Contract: <c>GET ~/Stocks</c> is an anonymous, user-facing browser whose
/// <c>page</c> is a client-supplied query parameter. A browse endpoint must not
/// answer an out-of-range page with HTTP 500 — at minimum it serves a usable
/// page. Implementation does <c>Skip((page - 1) * pageSize)</c>, so
/// <c>page=0</c> emits <c>OFFSET -50</c>, which PostgreSQL rejects (22023).
/// The existing pagination test only walks valid pages 1..10; the non-positive
/// boundary is unexercised.
/// </summary>
[Collection(WebHostCollection.Name)]
public class StocksControllerIndexNonPositivePageTests
{
    private readonly WebHostFixture _fixture;

    public StocksControllerIndexNonPositivePageTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Index_PageQueryIsZero_ServesPageInsteadOfServerError()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(new CommonStock { Ticker = "AAPL", Name = "Apple Inc." });
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks?page=0");

        response
            .StatusCode.Should()
            .Be(
                HttpStatusCode.OK,
                "an out-of-range page on the public stock browser must serve a usable "
                    + "page, not surface an unhandled OFFSET -50 PostgreSQL error as HTTP 500"
            );
    }
}
