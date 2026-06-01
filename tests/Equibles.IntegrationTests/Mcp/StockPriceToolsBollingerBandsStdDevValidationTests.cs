using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Mcp.Tools;
using Equibles.Yahoo.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Contract: GetBollingerBands requires stdDev &gt; 0 (a non-positive band width is
/// degenerate — zero collapses the bands onto the mean, negative inverts them). The
/// tool must reject it up front with a validation message, never compute bogus bands.
/// The period guard has a sibling pin (ATR); this stdDev guard is Bollinger-unique and
/// untested. Oracle derived from the band-width contract before reading the body.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockPriceToolsBollingerBandsStdDevValidationTests : ParadeDbMcpTestBase
{
    public StockPriceToolsBollingerBandsStdDevValidationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private StockPriceTools Sut() =>
        new(
            new DailyStockPriceRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<StockPriceTools>()
        );

    [Fact]
    public async Task GetBollingerBands_NonPositiveStdDev_ReturnsValidationMessage()
    {
        // stdDev = 0 is non-positive — the band width must be strictly positive, so the
        // tool returns the validation message rather than collapsing the bands.
        var result = await Sut().GetBollingerBands("AAPL", stdDev: 0m);

        result.Should().Contain("stdDev must be greater than 0");
    }
}
