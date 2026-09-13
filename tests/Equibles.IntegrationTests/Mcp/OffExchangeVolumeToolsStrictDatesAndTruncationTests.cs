using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Finra.Data.Models;
using Equibles.Finra.Mcp.Tools;
using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for GetOffExchangeVolume's date arguments and truncation signpost.
/// The old path silently substituted the default window for an unparseable date and
/// answered an inverted range with a factual-sounding "no data" claim; maxResults cut
/// weeks inside the requested range with no marker (the table renders oldest-first, so it
/// appeared to start where the range starts).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class OffExchangeVolumeToolsStrictDatesAndTruncationTests : ParadeDbMcpTestBase
{
    private OffExchangeVolumeTools Sut() =>
        new(
            new OffExchangeVolumeRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<OffExchangeVolumeTools>()
        );

    public OffExchangeVolumeToolsStrictDatesAndTruncationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private EquityIssuer AddGme()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GME",
            Name: "GameStop Corp",
            Cik: "0001326380"
        );
        DbContext.Set<EquityIssuer>().Add(stock);
        return stock;
    }

    private void AddWeek(EquityIssuer stock, DateOnly weekStart) =>
        DbContext
            .Set<OffExchangeVolume>()
            .Add(
                new OffExchangeVolume
                {
                    EquityListingId = Equibles
                        .TestSupport.NativeListingSeed.ForStock(
                            DbContext,
                            stock,
                            stock.Presentation.Listing.Ticker
                        )
                        .Id,
                    ListedTicker = stock.Presentation.Listing.Ticker,
                    WeekStartDate = weekStart,
                    AtsVolume = 5_000_000,
                    AtsTradeCount = 11_111,
                    NonAtsOtcVolume = 3_000_000,
                    NonAtsOtcTradeCount = 22_222,
                }
            );

    [Fact]
    public async Task GetOffExchangeVolume_UnparseableStartDate_ReturnsError()
    {
        AddGme();
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOffExchangeVolume("GME", startDate: "2026-06-31");

        result.Should().Be("Unknown startDate '2026-06-31'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetOffExchangeVolume_SecondaryListing_WithNoExactRows_DoesNotReturnPrimarySeries()
    {
        EquityIssuer stock = AddGme();
        Equibles.TestSupport.EquityIssuerSeed.SetSecondaryTickers(stock, ["GME-A"]);
        AddWeek(stock, new DateOnly(2026, 3, 16));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetOffExchangeVolume("GME-A");

        result.Should().Contain("No off-exchange volume data found for GME-A");
        result.Should().NotContain("2026-03-16");
        result.Should().NotContain("5,000,000");
    }

    [Fact]
    public async Task GetOffExchangeVolume_InvertedRange_ReturnsExplicitError()
    {
        EquityIssuer stock = AddGme();
        AddWeek(stock, new DateOnly(2026, 3, 16));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOffExchangeVolume("GME", startDate: "2026-06-30", endDate: "2026-01-01");

        result
            .Should()
            .Contain("startDate 2026-06-30 is after endDate 2026-01-01")
            .And.NotContain("No off-exchange volume data");
    }

    [Fact]
    public async Task GetOffExchangeVolume_TruncatedRange_AppendsNewestKeptNote()
    {
        EquityIssuer stock = AddGme();
        AddWeek(stock, new DateOnly(2026, 3, 2));
        AddWeek(stock, new DateOnly(2026, 3, 9));
        AddWeek(stock, new DateOnly(2026, 3, 16));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOffExchangeVolume(
                "GME",
                startDate: "2026-03-01",
                endDate: "2026-03-31",
                maxResults: 2
            );

        result.Should().Contain("Showing the newest 2 of 3 weeks");
        result.Should().Contain("2026-03-09");
        result.Should().NotContain("2026-03-02");
    }

    [Fact]
    public async Task GetOffExchangeVolume_CompleteRange_HasNoTruncationNote()
    {
        EquityIssuer stock = AddGme();
        AddWeek(stock, new DateOnly(2026, 3, 16));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOffExchangeVolume("GME", startDate: "2026-03-01", endDate: "2026-03-31");

        result.Should().NotContain("Showing the newest");
    }

    [Fact]
    public async Task GetOffExchangeVolume_AffectedHistoricalWeek_AppendsCoverageCaveat()
    {
        EquityIssuer stock = AddGme();
        AddWeek(stock, OffExchangeVolumeCoverage.CorrectedSymbolResolutionStartWeek.AddDays(-7));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOffExchangeVolume("GME", startDate: "2025-08-01", endDate: "2025-08-10");

        result.Should().Contain(OffExchangeVolumeCoverage.HistoricalCaseFoldCaveat);
    }

    [Fact]
    public async Task GetOffExchangeVolume_CorrectedBoundaryWeek_HasNoCoverageCaveat()
    {
        EquityIssuer stock = AddGme();
        AddWeek(stock, OffExchangeVolumeCoverage.CorrectedSymbolResolutionStartWeek);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOffExchangeVolume("GME", startDate: "2025-08-11", endDate: "2025-08-17");

        result.Should().NotContain("Historical data caveat");
    }

    [Fact]
    public async Task GetOffExchangeVolume_AffectedWeekExcludedByLimit_HasNoCoverageCaveat()
    {
        EquityIssuer stock = AddGme();
        AddWeek(stock, OffExchangeVolumeCoverage.CorrectedSymbolResolutionStartWeek.AddDays(-7));
        AddWeek(stock, OffExchangeVolumeCoverage.CorrectedSymbolResolutionStartWeek);
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetOffExchangeVolume(
                "GME",
                startDate: "2025-08-01",
                endDate: "2025-08-17",
                maxResults: 1
            );

        result.Should().NotContain("Historical data caveat");
        result.Should().Contain("Showing the newest 1 of 2 weeks");
    }
}
