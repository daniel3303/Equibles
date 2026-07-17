using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Adversarial cover for GetFailsToDeliver's date arguments and truncation signpost. The
/// old path silently substituted the default 3-month window for an unparseable date and
/// answered an inverted range with a factual-sounding "no data" claim; maxResults kept the
/// NEWEST rows while rendering oldest-first with no marker, so a truncated six-month range
/// read as "zero FTDs before the first displayed row".
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FailToDeliverToolsStrictDatesAndTruncationTests : ParadeDbMcpTestBase
{
    private FailToDeliverTools Sut() =>
        new(
            new FailToDeliverRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FailToDeliverTools>()
        );

    public FailToDeliverToolsStrictDatesAndTruncationTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CommonStock AddGme()
    {
        var stock = new CommonStock
        {
            Ticker = "GME",
            Name = "GameStop Corp",
            Cik = "0001326380",
        };
        DbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private void AddFtd(CommonStock stock, DateOnly settlementDate) =>
        DbContext
            .Set<FailToDeliver>()
            .Add(
                new FailToDeliver
                {
                    CommonStock = stock,
                    CommonStockId = stock.Id,
                    SettlementDate = settlementDate,
                    Quantity = 100_000,
                    Price = 25.50m,
                }
            );

    [Fact]
    public async Task GetFailsToDeliver_UnparseableStartDate_ReturnsError()
    {
        AddGme();
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFailsToDeliver("GME", startDate: "2026-1-32");

        result.Should().Be("Unknown startDate '2026-1-32'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetFailsToDeliver_UnparseableEndDate_ReturnsError()
    {
        AddGme();
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFailsToDeliver("GME", endDate: "next week");

        result.Should().Be("Unknown endDate 'next week'. Accepted: yyyy-MM-dd.");
    }

    [Fact]
    public async Task GetFailsToDeliver_InvertedRange_ReturnsExplicitError()
    {
        var stock = AddGme();
        AddFtd(stock, new DateOnly(2026, 4, 1));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFailsToDeliver("GME", startDate: "2026-06-30", endDate: "2026-01-01");

        result
            .Should()
            .Contain("startDate 2026-06-30 is after endDate 2026-01-01")
            .And.NotContain("No FTD data");
    }

    [Fact]
    public async Task GetFailsToDeliver_TruncatedRange_AppendsNewestKeptNote()
    {
        var stock = AddGme();
        AddFtd(stock, new DateOnly(2026, 4, 1));
        AddFtd(stock, new DateOnly(2026, 4, 2));
        AddFtd(stock, new DateOnly(2026, 4, 3));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFailsToDeliver(
                "GME",
                startDate: "2026-04-01",
                endDate: "2026-04-30",
                maxResults: 2
            );

        // The kept rows are the NEWEST two, displayed oldest-first — the note must say so,
        // or the table reads as "GME had no fails before 2026-04-02".
        result.Should().Contain("Showing the newest 2 of 3 settlement dates");
        result.Should().Contain("2026-04-02");
        result.Should().NotContain("2026-04-01 |");
    }

    [Fact]
    public async Task GetFailsToDeliver_CompleteRange_HasNoTruncationNote()
    {
        var stock = AddGme();
        AddFtd(stock, new DateOnly(2026, 4, 1));
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFailsToDeliver("GME", startDate: "2026-04-01", endDate: "2026-04-30");

        result.Should().NotContain("Showing the newest");
    }
}
