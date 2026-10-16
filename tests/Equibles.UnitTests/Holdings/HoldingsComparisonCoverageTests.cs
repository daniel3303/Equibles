using System.Globalization;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Models;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class HoldingsComparisonCoverageTests
{
    private static readonly DateOnly Prior = new(2025, 9, 30);
    private static readonly DateOnly Current = new(2025, 12, 31);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task History_MissingFilerOnEitherSide_ComparesLikeForLike(bool missingPrior)
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var stable = new InstitutionalHolder { Name = "Stable", Cik = "100" };
        var missing = new InstitutionalHolder { Name = "CoverageGapFiler", Cik = "200" };
        db.AddRange(stock, stable, missing);
        db.AddRange(
            Holding(stock, stable, Prior, 100),
            Holding(stock, stable, Current, 110),
            Holding(stock, missing, missingPrior ? Current : Prior, 900)
        );
        await db.SaveChangesAsync();
        var repository = new InstitutionalHoldingRepository(db);

        var coverage = await HoldingsComparisonCoverage.History(
            repository,
            stock,
            [Prior, Current],
            []
        );
        var status = coverage[Current];
        status.ComparisonAvailable.Should().BeTrue();
        status.CanCompareHolder(stable.Id).Should().BeTrue();
        status.CanCompareHolder(missing.Id).Should().BeFalse();
        status.ExcludedCurrentShares.Should().Be(missingPrior ? 900 : 0);
        status.ExcludedPreviousShares.Should().Be(missingPrior ? 0 : 900);
        status.LikeForLike.Should().Be((110L, 100L));
        var activity = await HoldingsComparisonCoverage.Activity(
            repository,
            [
                new HolderStockActivity
                {
                    InstitutionalHolderId = stable.Id,
                    CurrentPositionCount = 1,
                    PreviousPositionCount = 1,
                },
                new HolderStockActivity
                {
                    InstitutionalHolderId = missing.Id,
                    CurrentPositionCount = missingPrior ? 1 : 0,
                    PreviousPositionCount = missingPrior ? 0 : 1,
                },
            ],
            Current,
            Prior
        );
        // Only History sums both sides, so a ranking's status never claims a total change.
        activity.LikeForLike.Should().BeNull();

        var history = await Tools(db).GetInstitutionalOwnershipHistory("AAPL");
        history.Should().Contain("| +10.0% |").And.NotContain("unavailable (");
        history
            .Should()
            .Contain(
                missingPrior
                    ? "2025-12-31: Excluded 1 holder with no observed 2025-09-30 13F (900 shares); largest: CoverageGapFiler 900 (no 2025-09-30 13F)."
                    : "2025-12-31: Excluded 1 holder with no observed 2025-12-31 13F (900 shares as of 2025-09-30); largest: CoverageGapFiler 900 (no 2025-12-31 13F)."
            );
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers.Should().Contain("| Stable |").And.NotContain("| CoverageGapFiler |");
        movers.Should().Contain("largest: CoverageGapFiler 900");
    }

    [Fact]
    public async Task History_CombinedQuarter_ComparesHoldersYetToFileAtTheirCarriedPositions()
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var early = new InstitutionalHolder { Name = "EarlyFiler", Cik = "100" };
        var late = new InstitutionalHolder { Name = "LateFiler", Cik = "200" };
        db.AddRange(stock, early, late);
        db.AddRange(
            Holding(stock, early, Prior, 100),
            Holding(stock, early, Current, 110),
            Holding(stock, late, Prior, 900)
        );
        await db.SaveChangesAsync();

        var asFiled = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            []
        );
        var combined = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            [],
            combinedReportDate: Current
        );

        asFiled[Current].ExcludedPreviousShares.Should().Be(900);
        combined[Current].MissingCurrentFilers.Should().BeEmpty();
        combined[Current].Note.Should().BeNull();
        // The combined total carries LateFiler's 900 shares, so it stays on both sides.
        combined[Current].LikeForLike.Should().Be((1010L, 1000L));
    }

    [Fact]
    public async Task History_ExcludedShares_RestateOntoTodaysSplitBasisLikeTheTotals()
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var stable = new InstitutionalHolder { Name = "Stable", Cik = "100" };
        var gone = new InstitutionalHolder { Name = "Gone", Cik = "200" };
        db.AddRange(stock, stable, gone);
        db.AddRange(
            Holding(stock, stable, Prior, 100),
            Holding(stock, stable, Current, 220),
            Holding(stock, gone, Prior, 900)
        );
        await db.SaveChangesAsync();
        var split = new StockSplit
        {
            EquityIssuerId = stock.Id,
            PriceSeriesTicker = "AAPL",
            EffectiveDate = new DateOnly(2025, 11, 14),
            Numerator = 2,
            Denominator = 1,
        };

        var coverage = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            [split]
        );

        coverage[Current].ExcludedPreviousShares.Should().Be(1800);
        // Stable's 100 restates to 200 before the split; Gone's 1,800 leaves the prior side.
        coverage[Current].LikeForLike.Should().Be((220L, 200L));
    }

    [Fact]
    public void Note_NamesTheThreeLargestExclusionsWithInvariantNumbers()
    {
        var holders = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var status = new HoldingsComparisonStatus(
            Current,
            Prior,
            new HashSet<Guid> { holders[0], holders[1] },
            new HashSet<Guid> { holders[2], holders[3] }
        )
        {
            ExcludedFilers =
            [
                new(holders[0], "Newcomer", 750_000),
                new(holders[1], "Tiny", 70),
                new(holders[2], "NORGES BANK", 16_457_471),
                new(holders[3], "Lapsed", 184_038),
            ],
        };
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            status
                .Note.Should()
                .Be(
                    "Excluded 2 holders with no observed 2025-09-30 13F (750,070 shares) and 2 holders with no observed 2025-12-31 13F (16,641,509 shares as of 2025-09-30); largest: NORGES BANK 16,457,471 (no 2025-12-31 13F), Newcomer 750,000 (no 2025-09-30 13F), Lapsed 184,038 (no 2025-12-31 13F)."
                );
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FiledElsewhere_ProvesEntryOrExit_WithoutInferringFromShareCount(bool entry)
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        EquityIssuer other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MSFT",
            Name: "Microsoft",
            Cik: "789019"
        );
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        var stable = new InstitutionalHolder { Name = "Stable", Cik = "200" };
        db.AddRange(stock, other, holder, stable);
        db.AddRange(
            Holding(stock, stable, Prior, 100),
            Holding(stock, stable, Current, 100),
            Holding(stock, holder, entry ? Current : Prior, 900),
            Holding(other, holder, entry ? Prior : Current, 0)
        );
        await db.SaveChangesAsync();
        var coverage = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            []
        );
        coverage[Current].ComparisonAvailable.Should().BeTrue();
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers.Should().Contain("Reporter");
    }

    [Fact]
    public async Task ZeroHoldingsRestatement_ProvesTheExit()
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var stable = new InstitutionalHolder { Name = "Stable", Cik = "100" };
        var emptied = new InstitutionalHolder { Name = "Emptied", Cik = "200" };
        db.AddRange(stock, stable, emptied);
        db.AddRange(
            Holding(stock, stable, Prior, 100),
            Holding(stock, stable, Current, 110),
            Holding(stock, emptied, Prior, 900)
        );
        db.Add(
            new InstitutionalFiling
            {
                AccessionNumber = "0000000200-26-000001",
                InstitutionalHolderId = emptied.Id,
                ReportDate = Current,
                FilingDate = Current.AddDays(45),
                FilingType = FilingType.Form13F,
                IsAmendment = true,
                PositionCount = 0,
                DeclaredTotalValue = 0,
            }
        );
        await db.SaveChangesAsync();

        var coverage = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            []
        );

        coverage[Current].MissingCurrentFilers.Should().BeEmpty();
        coverage[Current].LikeForLike.Should().Be((110L, 1000L));
    }

    [Fact]
    public async Task Schedule13GDoesNotProvePrior13FFiling()
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        db.AddRange(stock, holder);
        var prior = Holding(stock, holder, Prior, 100);
        prior.FilingType = FilingType.Schedule13G;
        db.AddRange(prior, Holding(stock, holder, Current, 900));
        await db.SaveChangesAsync();
        var coverage = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            []
        );
        coverage[Current].MissingPreviousFilers.Should().Contain(holder.Id);
    }

    [Fact]
    public async Task NonconsecutiveQuarters_WithholdComparisonEvenForTheSameFiler()
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        db.AddRange(stock, holder);
        var older = new DateOnly(2025, 6, 30);
        db.AddRange(Holding(stock, holder, older, 100), Holding(stock, holder, Current, 110));
        await db.SaveChangesAsync();
        var coverage = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [older, Current],
            []
        );
        coverage[Current].ComparisonAvailable.Should().BeFalse();
        coverage[Current].CanCompareHolder(holder.Id).Should().BeFalse();
        coverage[Current].LikeForLike.Should().BeNull();
        coverage[Current].Note.Should().StartWith("Comparison unavailable");
    }

    [Fact]
    public async Task UnchangedComparableQuarter_DoesNotClaimThePriorQuarterIsMissing()
    {
        await using var db = NewDb();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var holder = new InstitutionalHolder { Name = "Reporter", Cik = "100" };
        db.AddRange(stock, holder);
        db.AddRange(Holding(stock, holder, Prior, 100), Holding(stock, holder, Current, 100));
        await db.SaveChangesAsync();
        var result = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        result
            .Should()
            .Contain("No comparable quarter-over-quarter movement")
            .And.NotContain("No prior quarter")
            .And.NotContain("Comparison unavailable");
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new ErrorsModuleConfiguration(),
            }
        );
        db.Database.EnsureCreated();
        return db;
    }

    private static InstitutionalHolding Holding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly date,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = date,
            FilingDate = date.AddDays(45),
            FilingType = FilingType.Form13F,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            AccessionNumber = Guid.NewGuid().ToString("N"),
        };

    private static InstitutionalHoldingsTools Tools(EquiblesFinancialDbContext db) =>
        new(
            new(db),
            new(db),
            new(db),
            new(db),
            new StockCombinedQuarterService(new(db), new(db)),
            new ErrorManager(new ErrorRepository(db)),
            NullLogger<InstitutionalHoldingsTools>.Instance
        );
}
