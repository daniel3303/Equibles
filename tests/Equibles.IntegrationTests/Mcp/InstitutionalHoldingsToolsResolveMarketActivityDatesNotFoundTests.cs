using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Sibling pin to <see cref="InstitutionalHoldingsToolsResolveMarketActivityDatesNoPriorQuarterTests"/>.
/// Covers the off-list arms of <c>ResolveMarketActivityDates</c>: a parseable
/// <c>reportDate</c> that isn't an available 13F quarter end snaps to the nearest
/// report ON OR BEFORE it and the substitution is stated in the output (standard
/// as-of semantics — never a silent fallback); a date OLDER than the tracked
/// history has nothing to snap to and returns an error listing the available
/// dates; an unparseable date returns a format correction.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsResolveMarketActivityDatesNotFoundTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsResolveMarketActivityDatesNotFoundTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetMostHeldStocks_ReportDateNotInAvailableDates_ReturnsNotFoundErrorListingAvailable()
    {
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "C1",
        };
        var filer = new InstitutionalHolder { Cik = "H1", Name = "Sole Filer" };
        DbContext.AddRange(aapl, filer);
        DbContext.Add(MakeHolding(aapl, filer, prior, shares: 100, value: 100_000));
        DbContext.Add(MakeHolding(aapl, filer, current, shares: 100, value: 200_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            new StockSplitRepository(verify),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(verify),
                new StockSplitRepository(verify)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        // A future off-list date snaps to the nearest report on or before it — the latest
        // quarter — with an explicit substitution note.
        var snapped = await sut.GetMostHeldStocks(reportDate: "2099-12-31");
        snapped.Should().Contain("Most-held 13F stocks as of 2024-12-31");
        snapped.Should().Contain("2099-12-31 is not a 13F report date");

        // A date older than the tracked history has nothing on or before it → an error that
        // lists the available dates instead of silently serving another quarter.
        var tooOld = await sut.GetMostHeldStocks(reportDate: "2001-03-31");
        tooOld.Should().Contain("No 13F report on or before 2001-03-31");
        tooOld.Should().Contain("2024-12-31");

        // An unparseable date returns a format correction, never the latest quarter.
        var unparseable = await sut.GetMostHeldStocks(reportDate: "Q4 2024");
        unparseable.Should().Contain("Could not parse reportDate 'Q4 2024'");
        unparseable.Should().NotContain("Most-held 13F stocks");
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        long shares,
        long value
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{stock.Ticker}-{reportDate:yyyyMMdd}",
        };
}
