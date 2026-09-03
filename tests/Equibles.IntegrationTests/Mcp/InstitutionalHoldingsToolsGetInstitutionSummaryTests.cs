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
/// Pins <c>GetInstitutionSummary</c>. Resolves the holder via the name-search query,
/// pulls current + prior quarter holdings, and feeds them to the same
/// <see cref="InstitutionPortfolioSummaryCalculator"/> the web profile uses. Each
/// <see cref="Fact"/> exercises one path so a regression in lookup or calculation
/// surfaces as a focused assertion.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionSummaryTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsGetInstitutionSummaryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionSummary_UnknownInstitution_ReportsNotFound()
    {
        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Definitely Not A Fund");

        output
            .Should()
            .Contain("No match for 'Definitely Not A Fund' in the tracked 13F filer set");
    }

    [Fact]
    public async Task GetInstitutionSummary_HolderWithNoHoldings_ReportsNoData()
    {
        DbContext.Add(new InstitutionalHolder { Cik = "00010001", Name = "Brand New Capital LP" });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Brand New");

        output.Should().Contain("No 13F holdings reported by Brand New Capital LP");
    }

    [Fact]
    public async Task GetInstitutionSummary_TwoQuarterHolder_RendersAllMetricsAndCaption()
    {
        var aapl = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var msft = new CommonStock
        {
            Ticker = "MSFT",
            Name = "Microsoft Corp.",
            Cik = "0000789019",
        };
        var holder = new InstitutionalHolder { Cik = "00010002", Name = "Big Fund LP" };
        DbContext.AddRange(aapl, msft, holder);
        var prior = new DateOnly(2024, 9, 30);
        var current = new DateOnly(2024, 12, 31);
        // Prior + current with movement on AAPL.
        DbContext.Add(MakeHolding(aapl, holder, prior, shares: 1_000, value: 1_000_000));
        DbContext.Add(MakeHolding(msft, holder, prior, shares: 500, value: 500_000));
        DbContext.Add(MakeHolding(aapl, holder, current, shares: 1_500, value: 1_500_000));
        DbContext.Add(MakeHolding(msft, holder, current, shares: 500, value: 500_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Big Fund");

        output.Should().Contain("Portfolio summary — **Big Fund LP** as of 2024-12-31");
        output.Should().Contain("vs prior quarter 2024-09-30");
        output.Should().Contain("Tracked 13F value");
        output.Should().Contain("# Positions");
        output.Should().Contain("Top 10 concentration");
        output.Should().Contain("Top 25 concentration");
        output.Should().Contain("QoQ turnover");
        output.Should().Contain("Quarters tracked");
        output.Should().Contain("_QoQ turnover = (");
        output.Should().Contain("2 × tracked 13F value");
    }

    [Fact]
    public async Task GetInstitutionSummary_ExplicitReportDate_HonorsArgumentWhenItMatches()
    {
        var stock = new CommonStock
        {
            Ticker = "TSLA",
            Name = "Tesla Inc.",
            Cik = "0001318605",
        };
        var holder = new InstitutionalHolder { Cik = "00010003", Name = "Targeted Capital" };
        DbContext.AddRange(stock, holder);
        var q3 = new DateOnly(2024, 9, 30);
        var q4 = new DateOnly(2024, 12, 31);
        DbContext.Add(MakeHolding(stock, holder, q3, shares: 100, value: 100_000));
        DbContext.Add(MakeHolding(stock, holder, q4, shares: 500, value: 500_000));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Targeted Capital", reportDate: "2024-09-30");

        output.Should().Contain("as of 2024-09-30");
        // No prior quarter further back, so the "vs prior" line MUST be absent.
        output.Should().NotContain("vs prior quarter");
    }

    [Fact]
    public async Task GetInstitutionSummary_AmbiguousPartialName_ReturnsCandidateCiks()
    {
        // The discovery result still ranks the larger flagship first, but an entity-scoped
        // tool must not silently select it when the caller supplied an ambiguous partial.
        var smallRia = new InstitutionalHolder { Cik = "00080001", Name = "Bridgewater Adv." };
        var flagship = new InstitutionalHolder
        {
            Cik = "00080002",
            Name = "Bridgewater Associates, LP",
        };
        DbContext.AddRange(smallRia, flagship);
        DbContext.AddRange(
            new InstitutionalFiling
            {
                AccessionNumber = "acc-ria-1",
                InstitutionalHolderId = smallRia.Id,
                FilingDate = new DateOnly(2025, 2, 14),
                ReportDate = new DateOnly(2024, 12, 31),
                PositionCount = 30,
                TotalValue = 500_000_000L,
            },
            new InstitutionalFiling
            {
                AccessionNumber = "acc-flagship-1",
                InstitutionalHolderId = flagship.Id,
                FilingDate = new DateOnly(2025, 2, 14),
                ReportDate = new DateOnly(2024, 12, 31),
                PositionCount = 300,
                TotalValue = 20_000_000_000L,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Bridgewater");

        output.Should().Contain("'Bridgewater' is ambiguous in the tracked 13F filer set");
        output.Should().Contain("Bridgewater Associates, LP (CIK 00080002");
        output.Should().Contain("Bridgewater Adv. (CIK 00080001");
        output.Should().Contain("tracked 13F value");
        output.Should().NotContain("reported AUM");
        output.Should().Contain("Pass the intended SEC CIK");
    }

    [Fact]
    public async Task GetInstitutionSummary_Schedule13GAtSame13FQuarter_ExcludesItFromReportedAum()
    {
        // A holder can file a Schedule 13G whose event date lands on a 13F quarter end. That
        // single disclosed stake shares the holdings table but is not part of the 13F
        // portfolio — reported AUM and position count must reflect the 13F holdings only,
        // never the 13F + 13G sum that doubled cross-filing holders' AUM (GH-3929).
        var apple = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var tesla = new CommonStock
        {
            Ticker = "TSLA",
            Name = "Tesla Inc.",
            Cik = "0001318605",
        };
        var holder = new InstitutionalHolder { Cik = "00090001", Name = "Crossfiling Capital" };
        DbContext.AddRange(apple, tesla, holder);
        var quarterEnd = new DateOnly(2026, 3, 31);
        // The real 13F portfolio: one position worth $1,000,000.
        DbContext.Add(MakeHolding(apple, holder, quarterEnd, shares: 1_000, value: 1_000_000));
        // A Schedule 13G stake reported at the same quarter end and worth far more — summing
        // it would inflate the AUM and add a phantom second position.
        DbContext.Add(
            new InstitutionalHolding
            {
                CommonStockId = tesla.Id,
                InstitutionalHolderId = holder.Id,
                FilingDate = quarterEnd.AddDays(45),
                ReportDate = quarterEnd,
                FilingType = FilingType.Schedule13G,
                Shares = 5_000,
                Value = 9_000_000,
                ShareType = ShareType.Shares,
                InvestmentDiscretion = InvestmentDiscretion.Sole,
                AccessionNumber = "acc-13g-tsla",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = NewSut(verify);

        var output = await sut.GetInstitutionSummary("Crossfiling Capital");

        output.Should().Contain("| Tracked 13F value | $1,000,000 |");
        output.Should().Contain("| # Positions | 1 |");
        output.Should().NotContain("10,000,000");
    }

    private InstitutionalHoldingsTools NewSut(Equibles.Data.EquiblesFinancialDbContext ctx) =>
        new(
            new InstitutionalHoldingRepository(ctx),
            new InstitutionalHolderRepository(ctx),
            new CommonStockRepository(ctx),
            new StockSplitRepository(ctx),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(ctx),
                new StockSplitRepository(ctx)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

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
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };
}
