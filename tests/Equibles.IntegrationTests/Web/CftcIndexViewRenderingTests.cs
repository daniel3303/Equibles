using System.Net;
using Equibles.Cftc.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to <see cref="SeededViewRenderingTests"/>: no test renders the Cftc
/// (Futures) Index view, so its compiled Razor view stays 0% along the populated
/// path. Pins the list: a seeded contract + latest position report must render
/// in its category table with the MarketCode, MarketName, a resolved
/// <c>asp-action="Show"</c> link, and the formatted CommercialNet (the
/// <c>CommercialNet.HasValue</c> branch — empty href / missing net = breakage).
/// </summary>
[Collection(WebHostCollection.Name)]
public class CftcIndexViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public CftcIndexViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetFutures_WithSeededContractAndReport_RendersContractRowWithCommercialNet()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            var contract = new CftcContract
            {
                MarketCode = "067651",
                MarketName = "CRUDE OIL, LIGHT SWEET-WTI",
                Category = CftcContractCategory.Energy,
            };
            db.Add(contract);
            db.Add(
                new CftcPositionReport
                {
                    CftcContract = contract,
                    ReportDate = new DateOnly(2026, 1, 6),
                    CommLong = 300_000,
                    CommShort = 100_000,
                    NonCommLong = 150_000,
                    NonCommShort = 120_000,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Futures");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("067651", "the seeded market code must render in the list");
        html.Should().Contain("CRUDE OIL, LIGHT SWEET-WTI", "the market name must render");
        html.Should()
            .Contain(
                "/futures/067651",
                "asp-action=\"Show\" must resolve to the (lowercased) Show route, not an empty href"
            );
        // N0 uses the host culture's group separator (a non-breaking space here),
        // HTML-encoded by Razor as &#xA0; — pins the CommercialNet.HasValue branch.
        html.Should()
            .Contain(
                "200&#xA0;000",
                "CommercialNet (CommLong - CommShort = 200000) must render formatted N0"
            );
    }
}
