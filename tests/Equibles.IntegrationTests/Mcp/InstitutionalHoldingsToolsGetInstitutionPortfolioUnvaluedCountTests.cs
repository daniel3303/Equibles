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
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the unvalued-position count behind GetInstitutionPortfolio's disclosure, on real
/// Postgres so the single grouped projection is proven translatable. The predicate must catch
/// every cohort of $0 rows: still pending, marked unknowable, AND rows the old retry ladder
/// abandoned with neither flag set (their only tell is the filer's own FiledValue). Counting
/// just the flagged rows once reported "1 unvalued position" for a filer with 94 of 96
/// positions at $0.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionPortfolioUnvaluedCountTests
    : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionPortfolioUnvaluedCountTests(
        ParadeDbFixture fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionPortfolio_CountsEveryUnvaluedCohortOnTheDistinctStockBasis()
    {
        var holder = new InstitutionalHolder { Cik = "77", Name = "Gotham Asset Management" };
        var valued = MakeStock("VALD");
        var pending = MakeStock("PEND");
        var unavailable = MakeStock("UNAV");
        var abandoned = MakeStock("ABND");
        var wortholess = MakeStock("ZERO");
        DbContext.AddRange(holder, valued, pending, unavailable, abandoned, wortholess);

        var reportDate = new DateOnly(2026, 3, 31);
        DbContext.AddRange(
            // A normally valued position.
            MakeHolding(holder, valued, reportDate, value: 5_000_000L),
            // Cohort 1: price still pending.
            MakeHolding(holder, pending, reportDate, value: 0L, valuePending: true),
            // Cohort 2: marked unknowable.
            MakeHolding(holder, unavailable, reportDate, value: 0L, valueUnavailable: true),
            // Cohort 3: the old ladder's give-up shape — both flags false, only the filer's
            // own figure betrays that the $0 is a hole, not a position worth nothing.
            MakeHolding(holder, abandoned, reportDate, value: 0L, filedValue: 2_500_000L),
            // A genuine $0 with no flags and no filed figure is NOT unvalued.
            MakeHolding(holder, wortholess, reportDate, value: 0L)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var output = await Sut().GetInstitutionPortfolio("Gotham");

        output.Should().Contain("3 tracked position(s) have no derivable value");
        output.Should().Contain("of 5 tracked positions");
    }

    private InstitutionalHoldingsTools Sut()
    {
        var verify = Fixture.CreateDbContext();
        return new InstitutionalHoldingsTools(
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
    }

    private static CommonStock MakeStock(string ticker) =>
        new()
        {
            Ticker = ticker,
            Name = $"{ticker} Corp",
            Cik = ticker.GetHashCode().ToString(),
        };

    private static InstitutionalHolding MakeHolding(
        InstitutionalHolder holder,
        CommonStock stock,
        DateOnly reportDate,
        long value,
        long? filedValue = null,
        bool valuePending = false,
        bool valueUnavailable = false
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = 1_000,
            Value = value,
            FiledValue = filedValue,
            ValuePending = valuePending,
            ValueUnavailable = valueUnavailable,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{stock.Ticker}",
        };
}
