using System.Globalization;
using Equibles.Fred.Data.Models;
using Equibles.Fred.Mcp.Tools;
using Equibles.Fred.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FredToolsGetEconomicIndicatorCultureInvarianceTests : ParadeDbMcpTestBase
{
    private FredTools Sut() =>
        new(
            new FredSeriesRepository(DbContext),
            new FredObservationRepository(DbContext),
            ErrorManager,
            NullLogger<FredTools>()
        );

    public FredToolsGetEconomicIndicatorCultureInvarianceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // GetEconomicIndicator renders each observation value with the culture-implicit
    // :G specifier, which honours the thread CurrentCulture. The established repo
    // contract (McpFormat.OrDash and the dozens of InvariantCulture MCP call sites:
    // "MCP markdown must not fork the separators by host locale") is that LLM-facing
    // markdown renders identically on every host. de-DE swaps the decimal separator
    // (5.25 → 5,25), forking the response — same bug class as #3013 / the VIX render.
    [Fact]
    public async Task GetEconomicIndicator_UnderNonInvariantCulture_RendersValueCultureInvariantly()
    {
        var series = new FredSeries
        {
            SeriesId = "FEDFUNDS",
            Title = "Federal Funds Effective Rate",
            Category = FredSeriesCategory.InterestRates,
            Frequency = "Monthly",
            Units = "Percent",
            SeasonalAdjustment = "Not Seasonally Adjusted",
        };
        DbContext.Set<FredSeries>().Add(series);
        DbContext
            .Set<FredObservation>()
            .Add(
                new FredObservation
                {
                    FredSeriesId = series.Id,
                    Date = new DateOnly(2025, 6, 1),
                    Value = 5.25m,
                }
            );
        await DbContext.SaveChangesAsync();

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            result = await Sut().GetEconomicIndicator("FEDFUNDS", "2025-01-01", "2025-12-31");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // The value cell must render with an en-US decimal point on every host
        // locale; de-DE would produce 5,25.
        result.Should().Contain("| 5.25 |");
    }
}
