using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the combined-quarter semantics every surface now resolves through
/// <see cref="StockCombinedQuarterService"/>. Scenario: previous quarter has holders A/B/C;
/// during the open filing window A re-files (resized), D files new, C files elsewhere without
/// the stock (a PROVEN exit), and B has not filed at all (carried forward). The presented
/// positions, reported-so-far figures, and split restatement must all read from that one story
/// — a carried non-filer must never count as a seller, a proven exit must.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class StockCombinedQuarterServiceTests : ParadeDbMcpTestBase
{
    public StockCombinedQuarterServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static readonly DateOnly Previous = new(2026, 3, 31);
    private static readonly DateOnly Current = new(2026, 6, 30);

    // Inside Current's 45-day filing window.
    private static readonly DateOnly InsideWindow = new(2026, 7, 15);

    // Past Current's deadline (Aug 14).
    private static readonly DateOnly AfterWindow = new(2026, 9, 1);

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
            CommonStock = stock,
            InstitutionalHolderId = holder.Id,
            InstitutionalHolder = holder,
            FilingDate = reportDate.AddDays(20),
            ReportDate = reportDate,
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            FilingType = FilingType.Form13F,
        };

    private async Task<(CommonStock Stock, StockCombinedQuarterService Service)> Seed()
    {
        var stock = new CommonStock { Ticker = "ARE", Name = "Alexandria Real Estate" };
        var other = new CommonStock { Ticker = "OTHR", Name = "Other Co" };
        var continuing = new InstitutionalHolder { Cik = "1", Name = "Continuing Fund" };
        var carried = new InstitutionalHolder { Cik = "2", Name = "Carried Fund" };
        var exited = new InstitutionalHolder { Cik = "3", Name = "Exited Fund" };
        var newcomer = new InstitutionalHolder { Cik = "4", Name = "Newcomer Fund" };
        DbContext.AddRange(stock, other, continuing, carried, exited, newcomer);

        // Previous quarter: A=100, B=200, C=300.
        DbContext.Add(MakeHolding(stock, continuing, Previous, shares: 100, value: 1_000));
        DbContext.Add(MakeHolding(stock, carried, Previous, shares: 200, value: 2_000));
        DbContext.Add(MakeHolding(stock, exited, Previous, shares: 300, value: 3_000));

        // Current quarter so far: A resized to 150, D new with 50; C filed — but only for the
        // OTHER stock, proving it dropped this one; B has not filed anything yet.
        DbContext.Add(MakeHolding(stock, continuing, Current, shares: 150, value: 1_500));
        DbContext.Add(MakeHolding(stock, newcomer, Current, shares: 50, value: 500));
        DbContext.Add(MakeHolding(other, exited, Current, shares: 999, value: 9_990));

        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var service = new StockCombinedQuarterService(
            new InstitutionalHoldingRepository(DbContext),
            new StockSplitRepository(DbContext)
        );
        return (stock, service);
    }

    [Fact]
    public async Task Resolve_InsideAndAfterTheFilingWindow_FlipsIsCombined()
    {
        var (stock, service) = await Seed();

        var open = await service.Resolve(stock, InsideWindow);
        open.ReportDate.Should().Be(Current);
        open.PreviousReportDate.Should().Be(Previous);
        open.IsCombined.Should().BeTrue("the 45-day window for Jun 30 runs until Aug 14");

        var closed = await service.Resolve(stock, AfterWindow);
        closed.IsCombined.Should().BeFalse("the window closed — the quarter is as-filed");
    }

    [Fact]
    public async Task GetPresentedPositions_WindowOpen_CarriesNonFilersAndDropsProvenExits()
    {
        var (stock, service) = await Seed();
        var anchor = await service.Resolve(stock, InsideWindow);

        var positions = await service.GetPresentedPositions(stock, anchor).ToListAsync();

        // A (current 150) + D (current 50) + B carried from Previous (200); C's prior row must
        // NOT carry — it filed the current quarter elsewhere, proving the exit.
        positions.Should().HaveCount(3);
        positions.Sum(h => h.Shares).Should().Be(400);
        positions
            .Where(h => h.ReportDate == Previous)
            .Should()
            .ContainSingle("only the non-filer's prior row is carried forward")
            .Which.Shares.Should()
            .Be(200);
    }

    [Fact]
    public async Task LoadReportedActivity_WindowOpen_CountsReportersOnly()
    {
        var (stock, service) = await Seed();
        var anchor = await service.Resolve(stock, InsideWindow);

        var reported = await service.LoadReportedActivity(stock, anchor);

        reported.PreviousHolderCount.Should().Be(3);
        // Reporters relevant to the stock: A (re-filed) + D (new) + C (proven exit). B is not
        // a reporter and must contribute NOTHING to the delta.
        reported.ReportedFilerCount.Should().Be(3);
        reported.NewFilerCount.Should().Be(1);
        reported.SoldOutFilerCount.Should().Be(1);
        // Net over reporters: (150 + 50) − (A's 100 + C's 300) = −200.
        reported.NetReportedShareDelta.Should().Be(-200);
        reported.CombinedHolderCount.Should().Be(3);
        reported.CombinedShares.Should().Be(400);
        reported.CombinedValue.Should().Be(4_000);
    }

    [Fact]
    public async Task LoadReportedActivity_SplitBetweenTheQuarters_RestatesBothSides()
    {
        var (stock, service) = await Seed();

        // 2:1 split between the two report dates: previous-quarter counts sit on the pre-split
        // basis and must double before they are compared or summed.
        DbContext.Add(
            new StockSplit
            {
                CommonStockId = stock.Id,
                EffectiveDate = new DateOnly(2026, 5, 15),
                Numerator = 2,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var anchor = await service.Resolve(stock, InsideWindow);
        var reported = await service.LoadReportedActivity(stock, anchor);

        // Reporters on today's basis: A went 200 (post-split) → 150 (−50), C exited −600,
        // D added +50 → net −600. Combined shares: 150 + 50 + B's carried 400 = 600.
        reported.NetReportedShareDelta.Should().Be(-600);
        reported.CombinedShares.Should().Be(600);
    }

    [Fact]
    public async Task LoadReportedActivity_AsFiledAnchor_Throws()
    {
        var (stock, service) = await Seed();
        var anchor = await service.Resolve(stock, AfterWindow);

        var act = () => service.LoadReportedActivity(stock, anchor);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
