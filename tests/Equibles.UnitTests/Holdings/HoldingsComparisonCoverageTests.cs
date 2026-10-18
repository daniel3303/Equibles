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

    [Fact]
    public async Task Succession_OneSuccessor_IsComparedWithTheFilerItContinues()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var old = Holder(db, "Old filer", "200");
        var renamed = Holder(db, "Renamed filer", "300");
        db.AddRange(
            Holding(stock, old, Prior, 900),
            Holding(stock, renamed, Current, 950),
            Succession(old, renamed, 1_000, 1_100)
        );
        await db.SaveChangesAsync();

        var status = (await History(db, stock))[Current];

        status.MissingPreviousFilers.Should().BeEmpty();
        status.MissingCurrentFilers.Should().BeEmpty();
        status.LikeForLike.Should().Be((1060L, 1000L));
        status.ComparisonKey(old.Id).Should().Be(renamed.Id);
        status.CanCompareHolder(old.Id).Should().BeFalse();
        status.CanCompareHolder(renamed.Id).Should().BeTrue();
        status
            .Note.Should()
            .Be("Compared across CIK changes: Old filer continues as Renamed filer.");
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers
            .Should()
            .Contain("| Renamed filer (continues Old filer) | +50 |")
            .And.Contain("| 900 → 950 |")
            .And.NotContain("| Old filer |")
            .And.NotContain("first 13F this quarter");
    }

    [Fact]
    public async Task Succession_SeveralSuccessors_CountOnlyInTheTotals()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var group = Holder(db, "Group filer", "200");
        var fiduciary = Holder(db, "Fiduciary filer", "300");
        var advisers = Holder(db, "Advisers filer", "400");
        db.AddRange(
            Holding(stock, group, Prior, 900),
            Holding(stock, fiduciary, Current, 500),
            Holding(stock, advisers, Current, 450),
            Succession(group, fiduciary, 1_000, 1_050),
            Succession(group, advisers, 1_000, 1_050)
        );
        await db.SaveChangesAsync();

        var status = (await History(db, stock))[Current];

        status.LikeForLike.Should().Be((1060L, 1000L));
        new[] { group.Id, fiduciary.Id, advisers.Id }
            .Should()
            .OnlyContain(id => !status.CanCompareHolder(id) && status.ComparisonKey(id) == id);
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers
            .Should()
            .Contain("| Stable |")
            .And.Contain("Group filer continues as 2 filers, not ranked one by one.")
            .And.NotContain("| Fiduciary filer")
            .And.NotContain("| Advisers filer")
            .And.NotContain("| Group filer");
    }

    [Fact]
    public async Task Succession_SeveralPredecessors_SumIntoTheSuccessorsPriorPosition()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var first = Holder(db, "First filer", "200");
        var second = Holder(db, "Second filer", "300");
        var parent = Holder(db, "Parent filer", "400");
        db.AddRange(
            Holding(stock, first, Prior, 400),
            Holding(stock, second, Prior, 500),
            Holding(stock, parent, Current, 950),
            Succession(first, parent, 1_000, 1_200),
            Succession(second, parent, 1_000, 1_200)
        );
        await db.SaveChangesAsync();

        (await History(db, stock))[Current].LikeForLike.Should().Be((1060L, 1000L));
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers
            .Should()
            .Contain("| Parent filer (continues 2 filers) | +50 |")
            .And.Contain("| 900 → 950 |")
            .And.Contain("Compared across CIK changes: 2 filers continue as Parent filer.");
    }

    [Fact]
    public async Task Succession_BooksTooFarApart_StaysExcluded()
    {
        // A successor that took a slice of the book would read as the group selling the rest.
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var old = Holder(db, "Old filer", "200");
        var slice = Holder(db, "Slice filer", "300");
        db.AddRange(
            Holding(stock, old, Prior, 900),
            Holding(stock, slice, Current, 950),
            Succession(old, slice, 1_000, 666)
        );
        await db.SaveChangesAsync();

        var status = (await History(db, stock))[Current];

        status.LikeForLike.Should().Be((110L, 100L));
        status.CanCompareHolder(slice.Id).Should().BeFalse();
        status
            .Note.Should()
            .Be(
                "Excluded 1 holder with no observed 2025-09-30 13F (950 shares) and 1 holder with no observed 2025-12-31 13F (900 shares as of 2025-09-30); largest: Slice filer 950 (CIK change, books not comparable), Old filer 900 (CIK change, books not comparable)."
            );
        status.SuccessionNote.Should().BeNull();
    }

    [Fact]
    public async Task Succession_NewPositionOfASuccessor_IsNamedAsTheContinuation()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var old = Holder(db, "Old filer", "200");
        var renamed = Holder(db, "Renamed filer", "300");
        var other = Holder(db, "Other filer", "400");
        var otherSuccessor = Holder(db, "Other successor", "500");
        db.AddRange(
            Holding(stock, renamed, Current, 950),
            Succession(old, renamed, 1_000, 1_100),
            // Neither filer of this group holds the stock, so its notes leave it out.
            Succession(other, otherSuccessor, 1_000, 1_100)
        );
        await db.SaveChangesAsync();

        var status = (await History(db, stock))[Current];

        status.LikeForLike.Should().Be((1060L, 100L));
        status
            .Note.Should()
            .Be("Compared across CIK changes: Old filer continues as Renamed filer.");
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers
            .Should()
            .Contain("| Renamed filer (continues Old filer) | +950 |")
            .And.NotContain("first 13F this quarter");
    }

    [Fact]
    public async Task Succession_SuccessorWithoutTheStock_SellsThePredecessorsPosition()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var old = Holder(db, "Old filer", "200");
        var renamed = Holder(db, "Renamed filer", "300");
        db.AddRange(Holding(stock, old, Prior, 900), Succession(old, renamed, 1_000, 1_100));
        await db.SaveChangesAsync();

        (await History(db, stock))[Current].LikeForLike.Should().Be((110L, 1000L));
        var movers = await Tools(db).GetTopInstitutionalBuyersSellers("AAPL", "2025-12-31");
        movers.Should().Contain("| Renamed filer (continues Old filer) | -900 |");
    }

    [Fact]
    public async Task Succession_SeveralToSeveral_IsComparedOnlyInTheTotals()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var first = Holder(db, "First filer", "200");
        var second = Holder(db, "Second filer", "300");
        var east = Holder(db, "East filer", "400");
        var west = Holder(db, "West filer", "500");
        db.AddRange(
            Holding(stock, first, Prior, 400),
            Holding(stock, second, Prior, 500),
            Holding(stock, east, Current, 450),
            Holding(stock, west, Current, 500),
            Succession(first, east, 1_000, 1_000),
            Succession(second, east, 1_000, 1_000),
            Succession(second, west, 1_000, 1_000)
        );
        await db.SaveChangesAsync();

        var status = (await History(db, stock))[Current];

        status.LikeForLike.Should().Be((1060L, 1000L));
        new[] { first.Id, second.Id, east.Id, west.Id }
            .Should()
            .OnlyContain(id => !status.CanCompareHolder(id) && status.ComparisonKey(id) == id);
        status
            .Note.Should()
            .Be(
                "Compared across CIK changes: 2 filers continue as 2 filers, not ranked one by one."
            );
    }

    [Fact]
    public async Task Succession_ComparableAndNotComparableGroups_ShareOneNote()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var old = Holder(db, "Old filer", "200");
        var renamed = Holder(db, "Renamed filer", "300");
        var parent = Holder(db, "Parent filer", "400");
        var slice = Holder(db, "Slice filer", "500");
        db.AddRange(
            Holding(stock, old, Prior, 900),
            Holding(stock, renamed, Current, 950),
            Holding(stock, parent, Prior, 50),
            Holding(stock, slice, Current, 5),
            Succession(old, renamed, 1_000, 1_100),
            Succession(parent, slice, 1_000, 10)
        );
        await db.SaveChangesAsync();

        var status = (await History(db, stock))[Current];
        status
            .Note.Should()
            .Be(
                "Excluded 1 holder with no observed 2025-09-30 13F (5 shares) and 1 holder with no observed 2025-12-31 13F (50 shares as of 2025-09-30); largest: Parent filer 50 (CIK change, books not comparable), Slice filer 5 (CIK change, books not comparable). Compared across CIK changes: Old filer continues as Renamed filer."
            );
        // The succession sentence alone, for surfaces that keep every other filer as new or sold out.
        status
            .SuccessionNote.Should()
            .Be("Compared across CIK changes: Old filer continues as Renamed filer.");
    }

    [Fact]
    public async Task Succession_PredecessorThatFiledAgain_IsNotMerged()
    {
        // The pair is stale until the next refresh drops it.
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var old = Holder(db, "Old filer", "200");
        var renamed = Holder(db, "Renamed filer", "300");
        db.AddRange(
            Holding(stock, old, Prior, 900),
            Holding(stock, old, Current, 900),
            Holding(stock, renamed, Current, 950),
            Succession(old, renamed, 1_000, 1_100),
            new InstitutionalFiling
            {
                AccessionNumber = "old-current",
                InstitutionalHolderId = old.Id,
                ReportDate = Current,
                FilingDate = Current.AddDays(45),
                FilingType = FilingType.Form13F,
                PositionCount = 1,
            }
        );
        await db.SaveChangesAsync();

        var status = (await History(db, stock))[Current];

        status.Successions.Should().BeEmpty();
        status.MissingPreviousFilers.Should().Contain(renamed.Id);
        status.LikeForLike.Should().Be((1010L, 1000L));
    }

    [Fact]
    public async Task Succession_NonconsecutiveQuarters_AreNotMerged()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var older = new DateOnly(2025, 6, 30);
        var old = Holder(db, "Old filer", "200");
        var renamed = Holder(db, "Renamed filer", "300");
        db.AddRange(
            Holding(stock, old, older, 900),
            Holding(stock, renamed, Current, 950),
            Succession(old, renamed, 1_000, 1_100)
        );
        await db.SaveChangesAsync();

        var status = (
            await HoldingsComparisonCoverage.History(new(db), stock, [older, Current], [])
        )[Current];

        status.Successions.Should().BeEmpty();
        status.Note.Should().StartWith("Comparison unavailable");
        status.SuccessionNote.Should().BeNull();
    }

    [Fact]
    public void SuccessionNote_NonconsecutiveQuarters_IsNullEvenWithAComparableGroup()
    {
        var old = Guid.NewGuid();
        var renamed = Guid.NewGuid();
        var status = new HoldingsComparisonStatus(
            Current,
            new DateOnly(2025, 6, 30),
            new HashSet<Guid>(),
            new HashSet<Guid>()
        )
        {
            Successions =
            [
                new(
                    Current,
                    new Dictionary<Guid, string> { [old] = "Old filer" },
                    new Dictionary<Guid, string> { [renamed] = "Renamed filer" },
                    Comparable: true
                ),
            ],
        };

        status.SuccessionNote.Should().BeNull();
    }

    [Fact]
    public void SuccessionNote_NameEndingInAPeriod_EndsTheSentenceOnce()
    {
        var status = new HoldingsComparisonStatus(
            Current,
            Prior,
            new HashSet<Guid>(),
            new HashSet<Guid>()
        )
        {
            Successions =
            [
                new(
                    Current,
                    new Dictionary<Guid, string> { [Guid.NewGuid()] = "BlackRock Inc." },
                    new Dictionary<Guid, string> { [Guid.NewGuid()] = "BlackRock, Inc." },
                    Comparable: true
                ),
            ],
        };

        status
            .SuccessionNote.Should()
            .Be("Compared across CIK changes: BlackRock Inc. continues as BlackRock, Inc.");
        status.Note.Should().Be(status.SuccessionNote);
    }

    [Fact]
    public async Task Succession_CombinedQuarter_DoesNotCarryTheContinuedFiler()
    {
        await using var db = NewDb();
        var (stock, _) = SeedStable(db);
        var old = Holder(db, "Old filer", "200");
        var renamed = Holder(db, "Renamed filer", "300");
        db.AddRange(
            Holding(stock, old, Prior, 900),
            Holding(stock, renamed, Current, 950),
            Succession(old, renamed, 1_000, 1_100)
        );
        await db.SaveChangesAsync();

        var combined = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            [],
            combinedReportDate: Current
        );

        combined[Current].LikeForLike.Should().Be((1060L, 1000L));
    }

    [Theory]
    [InlineData(1_000, 1_500, true)]
    [InlineData(1_500, 1_000, true)]
    [InlineData(1_000, 1_501, false)]
    [InlineData(1_501, 1_000, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 1_000, false)]
    public void IsComparable_KeepsBooksWithinTheRatio(
        long predecessor,
        long successor,
        bool comparable
    )
    {
        HoldingsSuccessionGroups.IsComparable(predecessor, successor).Should().Be(comparable);
    }

    [Fact]
    public void Build_JoinsPairsSharingAFilerWithinOneQuarter()
    {
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        HoldingsSuccessionPair Pair(
            int from,
            int to,
            DateOnly quarter,
            long successorValue = 1_000
        ) =>
            new()
            {
                PredecessorHolderId = ids[from],
                PredecessorName = $"P{from}",
                SuccessorHolderId = ids[to],
                SuccessorName = $"S{to}",
                SuccessorFirstReportDate = quarter,
                PredecessorGroupValue = 1_000,
                SuccessorGroupValue = successorValue,
            };

        var groups = HoldingsSuccessionGroups.Build([
            Pair(0, 1, Current),
            Pair(2, 3, Current),
            // Joins the first pair through its successor.
            Pair(2, 1, Current),
            Pair(4, 5, Prior, successorValue: 10),
        ]);

        groups.Should().HaveCount(2);
        var joined = groups.Single(group => group.ReportDate == Current);
        joined.Predecessors.Keys.Should().BeEquivalentTo([ids[0], ids[2]]);
        joined.Successors.Keys.Should().BeEquivalentTo([ids[1], ids[3]]);
        joined.Comparable.Should().BeTrue();
        groups.Single(group => group.ReportDate == Prior).Comparable.Should().BeFalse();
    }

    private static (EquityIssuer Stock, InstitutionalHolder Stable) SeedStable(
        EquiblesFinancialDbContext db
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "320193"
        );
        var stable = new InstitutionalHolder { Name = "Stable", Cik = "100" };
        db.AddRange(stock, stable);
        db.AddRange(Holding(stock, stable, Prior, 100), Holding(stock, stable, Current, 110));
        return (stock, stable);
    }

    private static InstitutionalHolder Holder(
        EquiblesFinancialDbContext db,
        string name,
        string cik
    )
    {
        var holder = new InstitutionalHolder { Name = name, Cik = cik };
        db.Add(holder);
        return holder;
    }

    private static HolderSuccession Succession(
        InstitutionalHolder predecessor,
        InstitutionalHolder successor,
        long predecessorValue,
        long successorValue
    ) =>
        new()
        {
            PredecessorHolderId = predecessor.Id,
            SuccessorHolderId = successor.Id,
            PredecessorLastReportDate = Prior,
            SuccessorFirstReportDate = Current,
            Basis = HolderSuccessionBasis.NoticeReportedBy,
            PredecessorGroupValue = predecessorValue,
            SuccessorGroupValue = successorValue,
            DerivationVersion = 1,
        };

    private static Task<Dictionary<DateOnly, HoldingsComparisonStatus>> History(
        EquiblesFinancialDbContext db,
        EquityIssuer stock
    ) => HoldingsComparisonCoverage.History(new(db), stock, [Prior, Current], []);

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
