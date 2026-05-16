using System.Net;
using Equibles.Fred.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// Sibling to <see cref="SeededViewRenderingTests"/>: that suite covers
/// <c>EconomicData/Show</c> but never the <c>EconomicData/Index</c> list view,
/// so its compiled Razor view stays 0% along its populated path. This pins the
/// list: a seeded series must render in its category table with the SeriesId,
/// Title, and a resolved <c>asp-action="Show"</c> link (an empty href would
/// mean a broken route/tag-helper — silent navigation breakage). The app
/// lowercases generated URLs, so the resolved Show href is
/// <c>/economicdata/fedfunds</c> while the cell text stays uppercase.
/// </summary>
[Collection(WebHostCollection.Name)]
public class EconomicDataIndexViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public EconomicDataIndexViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetEconomicDataIndex_WithSeededSeries_RendersSeriesRowWithResolvedShowLink()
    {
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new FredSeries
                {
                    SeriesId = "FEDFUNDS",
                    Title = "Federal Funds Effective Rate",
                    Category = FredSeriesCategory.InterestRates,
                    Frequency = "M",
                    Units = "Percent",
                    SeasonalAdjustment = "Not Seasonally Adjusted",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/EconomicData");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("FEDFUNDS", "the seeded series id must render in the list");
        html.Should().Contain("Federal Funds Effective Rate", "the series title must render");
        html.Should()
            .Contain(
                "/economicdata/fedfunds",
                "asp-action=\"Show\" must resolve to the (lowercased) Show route, not an empty href"
            );
    }
}
