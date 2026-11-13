using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Extensions;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Holdings;

// A filer whose 13F the top of its stored reporting chain takes over continues under that filer,
// only across a seamless handover of a comparable book; everything else keeps its own quarters.
public class HolderReportedBookQueriesTests : IDisposable
{
    private static readonly DateOnly Q1 = new(2026, 3, 31);
    private static readonly DateOnly Q2 = new(2026, 6, 30);
    private static readonly DateOnly Q3 = new(2026, 9, 30);

    private readonly EquiblesFinancialDbContext _dbContext;

    public HolderReportedBookQueriesTests()
    {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new CorporateActionsModuleConfiguration()
        );
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task AFilerWhoseParentTakesOverItsBook_ContinuesUnderTheParent()
    {
        var fund = Holder("1000001", "Fund Management, L.P.");
        var parent = Holder("1000002", "Fund Inc.");
        Quarters(fund, (Q1, 13_700));
        // The parent filed a small book of its own before it took the fund's over.
        Quarters(parent, (Q1, 570), (Q2, 19_400), (Q3, 20_000));
        ReportsThrough(fund, parent);
        await _dbContext.SaveChangesAsync();

        var book = (await Books(fund))[fund.Id];

        book.Continued.Should().BeTrue();
        book.Reporter.Id.Should().Be(parent.Id);
        book.Quarters.Select(quarter => (quarter.ReportDate, quarter.Aum))
            .Should()
            .Equal((Q3, 20_000), (Q2, 19_400), (Q1, 13_700));
    }

    [Fact]
    public async Task AParentReportingOtherBooksToo_OrAGapOrALiveFiler_KeepsItsOwnQuarters()
    {
        var affiliate = Holder("2000001", "Affiliate Advisers");
        var conglomerate = Holder("2000002", "Conglomerate Group");
        Quarters(affiliate, (Q1, 10_000));
        Quarters(conglomerate, (Q1, 90_000), (Q2, 100_000));
        ReportsThrough(affiliate, conglomerate);

        var dormant = Holder("3000001", "Dormant Capital");
        var laterParent = Holder("3000002", "Later Parent");
        Quarters(dormant, (Q1, 10_000));
        Quarters(laterParent, (Q3, 10_500));
        ReportsThrough(dormant, laterParent);

        var live = Holder("4000001", "Live Capital");
        var liveParent = Holder("4000002", "Live Parent");
        Quarters(live, (Q1, 10_000), (Q2, 10_100), (Q3, 10_200));
        Quarters(liveParent, (Q2, 10_100), (Q3, 10_200));
        ReportsThrough(live, liveParent);

        var alone = Holder("5000001", "No Parent Capital");
        Quarters(alone, (Q1, 10_000));
        await _dbContext.SaveChangesAsync();

        var books = await Books(affiliate, dormant, live, alone);

        books.Values.Should().OnlyContain(book => !book.Continued);
        books[live.Id].Quarters.Should().HaveCount(3);
        books[affiliate.Id].Quarters.Single().Aum.Should().Be(10_000);
    }

    private Task<Dictionary<Guid, Equibles.Holdings.Repositories.Models.HolderReportedBook>> Books(
        params InstitutionalHolder[] filers
    ) =>
        new InstitutionalHoldingRepository(_dbContext).GetReportedBooks(
            new InstitutionalHolderRepository(_dbContext),
            filers
        );

    private InstitutionalHolder Holder(string cik, string name)
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = name };
        _dbContext.Add(holder);
        return holder;
    }

    private void Quarters(InstitutionalHolder holder, params (DateOnly Date, long Aum)[] quarters)
    {
        foreach (var (date, aum) in quarters)
            _dbContext.Add(
                new HolderQuarterlySnapshot
                {
                    InstitutionalHolderId = holder.Id,
                    ReportDate = date,
                    FilingDate = date.AddDays(45),
                    Aum = aum,
                    PositionCount = 1,
                    StockCount = 1,
                }
            );
    }

    private void ReportsThrough(InstitutionalHolder filer, InstitutionalHolder parent) =>
        _dbContext.Add(
            new HolderRollup
            {
                Cik = filer.Cik,
                ReportingParentCik = parent.Cik,
                ReportingTopCik = parent.Cik,
                UltimateCik = parent.Cik,
                UltimateHolderId = parent.Id,
            }
        );
}
