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
/// Measured style bands reach GetTopHolders, GetInstitutionSummary and GetInstitutionStyleHistory
/// on Postgres, read from one measured quarter on or before the requested one.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsStyleTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly September = new(2024, 9, 30);
    private static readonly DateOnly December = new(2024, 12, 31);
    private static readonly DateOnly March = new(2025, 3, 31);

    public InstitutionalHoldingsToolsStyleTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetTopHolders_ShowsEachHoldersStyleAndFiltersByIt()
    {
        var stock = Stock();
        var focused = Holder("1", "Focused Capital");
        var tracker = Holder("2", "Tracker Advisers");
        var unmeasured = Holder("3", "New Filer LLC");
        DbContext.AddRange(
            Holding(stock, focused, 6_000, December),
            Holding(stock, tracker, 3_000, December),
            Holding(stock, unmeasured, 1_000, December),
            Metrics(focused, December, HolderStyleBand.Concentrated),
            Metrics(tracker, December, HolderStyleBand.IndexLike)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var all = await Sut(verify).GetTopHolders("AAPL");
        var picked = await Sut(verify).GetTopHolders("AAPL", styles: ["conc", "IDX"]);
        var both = await Sut(verify).GetTopHolders("AAPL", holderTypes: ["unk"], styles: ["idx"]);
        var none = await Sut(verify).GetTopHolders("AAPL", styles: ["unk"]);
        var refused = await Sut(verify).GetTopHolders("AAPL", styles: ["growth"]);

        all.Should()
            .Contain("| Holder Type | Style |")
            .And.Contain(
                "| 1 | Focused Capital | Common | 6,000 | 0.6 | 60.00% | — | Concentrated |"
            )
            .And.Contain("| Tracker Advisers | Common | 3,000 | 0.3 | 30.00% | — | Index-like |")
            .And.Contain("| New Filer LLC | Common | 1,000 | 0.1 | 10.00% | — | — |")
            .And.Contain(
                "_Style is each holder's band measured from its filed 13F book for 2024-12-31, never the firm's own description;"
            );
        picked
            .Should()
            .Contain("Showing rows 1-2 of 2 from style idx, conc, across 2 institutions.")
            .And.Contain("30.00%")
            .And.NotContain("New Filer LLC");
        both.Should()
            .Contain("from holder type unk and style idx, across 1 institutions.")
            .And.Contain("| 1 | Tracker Advisers |")
            .And.NotContain("Focused Capital");
        none.Should().Be("No institutional holders of AAPL with style unk as of 2024-12-31.");
        refused.Should().StartWith("Unknown style 'growth'.");
    }

    [Fact]
    public async Task GetTopHolders_ReadsTheNewestMeasuredQuarterOnOrBeforeTheReport()
    {
        var stock = Stock();
        var focused = Holder("1", "Focused Capital");
        var later = Holder("2", "Later Measured LP");
        DbContext.AddRange(
            Holding(stock, focused, 6_000, December),
            Holding(stock, later, 3_000, December),
            Metrics(focused, September, HolderStyleBand.Concentrated),
            Metrics(later, March, HolderStyleBand.IndexLike)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var output = await Sut(verify).GetTopHolders("AAPL");

        output
            .Should()
            .Contain("| Focused Capital | Common | 6,000 | 0.6 | 66.67% | — | Concentrated |")
            .And.Contain("| Later Measured LP | Common | 3,000 | 0.3 | 33.33% | — | — |")
            .And.Contain("for 2024-09-30, the newest measured quarter, never");
    }

    [Fact]
    public async Task GetInstitutionStyleHistory_ListsMeasuredQuartersNewestFirst()
    {
        var stock = Stock();
        var old = Holder("1364742", "Focused Capital (old)");
        var holder = Holder("2012383", "Focused Capital");
        DbContext.AddRange(
            Holding(stock, old, 6_000, new DateOnly(2024, 6, 30)),
            Holding(stock, holder, 6_000, December),
            Holding(stock, holder, 6_000, March),
            Metrics(holder, September, HolderStyleBand.DiversifiedActive),
            Metrics(holder, December, HolderStyleBand.Concentrated),
            new HolderSuccession
            {
                PredecessorHolderId = old.Id,
                SuccessorHolderId = holder.Id,
                PredecessorLastReportDate = new DateOnly(2024, 6, 30),
                SuccessorFirstReportDate = September,
                Basis = HolderSuccessionBasis.SameIncludedManagers,
                PredecessorGroupValue = 1,
                SuccessorGroupValue = 1,
                DerivationVersion = 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var output = await Sut(verify).GetInstitutionStyleHistory("2012383");
        var latestOnly = await Sut(verify).GetInstitutionStyleHistory("2012383", maxPeriods: 1);
        var predecessor = await Sut(verify).GetInstitutionStyleHistory("1364742");

        output
            .Should()
            .StartWith(
                "Style history of **Focused Capital** (CIK 2012383), 2 measured quarters through 2024-12-31"
            )
            .And.Contain(
                "Note: its 2025-03-31 13F is not measured yet; a quarter is measured once its filing window closes."
            )
            .And.Contain(
                "| 2024-12-31 | Concentrated | Medium turnover | Long horizon | 1,000.0 | 300 | 70.0% | 65.0% | 9.5 | 50.0% | 14.2 |"
            )
            .And.Contain("| 2024-09-30 | Diversified active |")
            .And.Contain("**Latest quarter, 2024-12-31:**")
            .And.Contain("- Concentrated: the top 10 positions are 65.0% of the book (60%+)")
            .And.Contain("- Medium turnover: 50.0% over the last four quarters")
            .And.Contain(
                "_Style bands measure the filed 13F common-share book each closed quarter."
            )
            .And.Contain(
                "_Quarters through 2024-06-30 were filed under Focused Capital (old) (CIK 1364742); pass that CIK for its earlier style history._"
            );
        output
            .IndexOf("| 2024-12-31 |", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("| 2024-09-30 |", StringComparison.Ordinal));
        latestOnly.Should().Contain("1 measured quarters").And.NotContain("| 2024-09-30 |");
        predecessor
            .Should()
            .StartWith("No style measurements for **Focused Capital (old)** (CIK 1364742) yet;")
            .And.Contain(
                "_From 2024-09-30 these 13F reports continue under Focused Capital (CIK 2012383); pass that CIK for later quarters._"
            );
    }

    [Fact]
    public async Task GetInstitutionStyleHistory_UnmeasuredHolder_SaysWhen()
    {
        var stock = Stock();
        var holder = Holder("1", "New Filer LLC");
        DbContext.Add(Holding(stock, holder, 1_000, December));
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var output = await Sut(verify).GetInstitutionStyleHistory("New Filer LLC");

        output
            .Should()
            .Be(
                "No style measurements for **New Filer LLC** (CIK 1) yet; a quarter is measured once its filing window closes.\n"
            );
    }

    [Fact]
    public async Task GetInstitutionSummary_ShowsTheBandsOfTheNewestMeasuredQuarter()
    {
        var stock = Stock();
        var holder = Holder("1", "Focused Capital");
        var unmeasured = Holder("2", "New Filer LLC");
        DbContext.AddRange(
            Holding(stock, holder, 6_000, December),
            Holding(stock, holder, 6_000, March),
            Holding(stock, unmeasured, 1_000, March),
            Metrics(holder, December, HolderStyleBand.Concentrated)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var latest = await Sut(verify).GetInstitutionSummary("Focused Capital");
        var december = await Sut(verify).GetInstitutionSummary("Focused Capital", "2024-12-31");
        var none = await Sut(verify).GetInstitutionSummary("New Filer LLC");

        latest
            .Should()
            .Contain(
                "**Style** (measured from the filed 13F book for 2024-12-31, the newest measured quarter, never the firm's own description):"
            )
            .And.Contain(
                "| Style | Concentrated: the top 10 positions are 65.0% of the book (60%+) |"
            )
            .And.Contain(
                "| Horizon | Long horizon: value-weighted holding period 14.2 quarters (12+) |"
            );
        december
            .Should()
            .Contain(
                "**Style** (measured from the filed 13F book for 2024-12-31, never the firm's own description):"
            );
        none.Should()
            .Contain(
                "_No quarter on or before 2025-03-31 is measured yet; a quarter is measured once its filing window closes._"
            );
    }

    private EquityIssuer Stock()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        DbContext.Add(stock);
        return stock;
    }

    private InstitutionalHolder Holder(string cik, string name)
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = name };
        DbContext.Add(holder);
        return holder;
    }

    private static HolderQuarterlyMetrics Metrics(
        InstitutionalHolder holder,
        DateOnly reportDate,
        HolderStyleBand style
    ) =>
        new()
        {
            InstitutionalHolderId = holder.Id,
            ReportDate = reportDate,
            EquityAum = 1_000_000_000,
            StockCount = 300,
            AggregateActiveShare = 0.70m,
            Turnover = 0.12m,
            TrailingAnnualTurnover = 0.50m,
            TopTenWeight = 0.65m,
            EffectivePositions = 9.5m,
            ValueWeightedTenureQuarters = 14.2m,
            CensoredValueShare = 0m,
            StyleBand = style,
            IndexLikeEvidence =
                style == HolderStyleBand.IndexLike
                    ? HolderIndexLikeEvidence.ActiveShare
                    : HolderIndexLikeEvidence.None,
            TurnoverBand = HolderTurnoverBand.Medium,
            HorizonBand = HolderHorizonBand.Long,
            BandVersion = 1,
            CalculationVersion = 1,
        };

    private static InstitutionalHolding Holding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        long shares,
        DateOnly reportDate
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}-{reportDate:yyyyMMdd}",
        };

    private InstitutionalHoldingsTools Sut(Equibles.Data.EquiblesFinancialDbContext db) =>
        new(
            new InstitutionalHoldingRepository(db),
            new InstitutionalHolderRepository(db),
            new EquityIssuerRepository(db),
            new StockSplitRepository(db),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(db),
                new StockSplitRepository(db)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );
}
