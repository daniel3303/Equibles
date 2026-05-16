using System.Net;
using Equibles.Cboe.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Market/Vix and Market/PutCallRatio views are pinned elsewhere, but the
/// Market/Index dashboard (<c>~/Market</c>) is never rendered — its compiled
/// Razor view stays 0% along the populated path. Pins it: seeded put/call +
/// VIX rows must render the title, the VIX card's latest date, and a resolved
/// (lowercased) PutCallRatio link. Culture-safe assertions only (no N2
/// decimals — the host formats with a comma decimal separator).
/// </summary>
[Collection(WebHostCollection.Name)]
public class MarketIndexViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public MarketIndexViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMarket_WithSeededPutCallAndVix_RendersPutCallLinkAndVixCard()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CboePutCallRatio
                {
                    RatioType = CboePutCallRatioType.Equity,
                    Date = new DateOnly(2026, 1, 6),
                    CallVolume = 1_200_000,
                    PutVolume = 900_000,
                    TotalVolume = 2_100_000,
                    PutCallRatio = 0.75m,
                }
            );
            db.AddRange(
                new CboeVixDaily
                {
                    Date = new DateOnly(2026, 1, 5),
                    Open = 13.0m,
                    High = 14.0m,
                    Low = 12.8m,
                    Close = 13.5m,
                },
                new CboeVixDaily
                {
                    Date = new DateOnly(2026, 1, 6),
                    Open = 13.5m,
                    High = 15.1m,
                    Low = 13.4m,
                    Close = 14.9m,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Market");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Market Indicators", "the Index page title must render");
        html.Should()
            .Contain(
                "2026-01-06",
                "the VIX card must render the latest seeded VIX date (invariant yyyy-MM-dd)"
            );
        html.Should()
            .Contain(
                "/market/putcallratio/",
                "the put/call loop must render a resolved (lowercased) PutCallRatio link"
            );
    }
}
