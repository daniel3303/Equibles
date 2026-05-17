using System.Net;
using System.Text.Encodings.Web;
using Equibles.Cftc.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to <see cref="SeededViewRenderingTests"/>: no test renders the Cftc
/// (Futures) Show view, so its compiled Razor view stays 0% along the populated
/// path. Pins it: a seeded contract + report must render the latest-stats cards
/// (OpenInterest N0), the reports table row, and the chart canvas the inline JS
/// binds to (a missing #cftc-chart silently breaks the visualization).
/// </summary>
[Collection(WebHostCollection.Name)]
public class CftcShowViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public CftcShowViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetFuturesShow_WithSeededContractAndReport_RendersStatsTableAndChartCanvas()
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
                    OpenInterest = 250_000,
                    CommLong = 300_000,
                    CommShort = 100_000,
                    NonCommLong = 150_000,
                    NonCommShort = 120_000,
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Futures/067651");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("CRUDE OIL, LIGHT SWEET-WTI", "the market name must render");
        // Razor formats with the host culture's N0 separator and HTML-encodes it.
        // The separator char is ICU/culture-dependent (U+00A0 vs U+202F across
        // runtimes), so compute the expected token with the same encoder Razor
        // uses instead of hardcoding one separator.
        var expectedOpenInterest = HtmlEncoder.Default.Encode(250_000.ToString("N0"));
        html.Should()
            .Contain(expectedOpenInterest, "the latest OpenInterest stat must render formatted N0");
        html.Should().Contain("2026-01-06", "the seeded report row date must render in the table");
        html.Should()
            .Contain(
                "id=\"cftc-chart\"",
                "the chart canvas the inline JS binds to must be present"
            );
    }
}
