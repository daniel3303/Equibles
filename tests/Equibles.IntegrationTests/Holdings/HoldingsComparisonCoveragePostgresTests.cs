using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HoldingsComparisonCoveragePostgresTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData(4)]
    [InlineData(20)]
    public async Task History_BatchesEvidenceRegardlessOfQuarterCount(int quarterCount)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "COVER",
            Name: "Coverage subject"
        );
        EquityIssuer other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHER",
            Name: "Other issuer"
        );
        var stable = new InstitutionalHolder { Cik = "1", Name = "Stable filer" };
        var rotating = new InstitutionalHolder { Cik = "2", Name = "Rotating filer" };
        var dates = Enumerable
            .Range(0, quarterCount)
            .Select(i => new DateOnly(2020, 1, 1).AddMonths((i + 1) * 3).AddDays(-1))
            .ToArray();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, other, stable, rotating);
            for (var i = 0; i < dates.Length; i++)
            {
                seed.Add(Holding(stock, stable, dates[i]));
                seed.Add(Holding(i % 2 == 0 ? stock : other, rotating, dates[i]));
            }
            await seed.SaveChangesAsync();
        }
        var counter = new HoldingsReadCounter();
        await using var db = fixture.CreateDbContext(options => options.AddInterceptors(counter));
        var result = await HoldingsComparisonCoverage.History(
            new InstitutionalHoldingRepository(db),
            stock,
            dates,
            []
        );
        result.Should().HaveCount(quarterCount - 1);
        result.Values.Should().OnlyContain(status => status.ComparisonAvailable);
        counter
            .Reads.Should()
            .Be(2, "position presence and missing-side evidence each use one batched read");
    }

    [Fact]
    public async Task History_LikeForLikeTotalsAndNamesLoadInBatchedReads()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "COVER",
            Name: "Coverage subject"
        );
        var stable = new InstitutionalHolder { Cik = "1", Name = "Stable filer" };
        var lapsing = new InstitutionalHolder { Cik = "2", Name = "Lapsing filer" };
        var dates = Enumerable
            .Range(0, 4)
            .Select(i => new DateOnly(2020, 1, 1).AddMonths((i + 1) * 3).AddDays(-1))
            .ToArray();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, stable, lapsing);
            foreach (var date in dates)
                seed.Add(Holding(stock, stable, date));
            // Files only in alternate quarters, so every comparison misses it on one side.
            seed.Add(Holding(stock, lapsing, dates[0]));
            seed.Add(Holding(stock, lapsing, dates[2]));
            await seed.SaveChangesAsync();
        }
        var counter = new HoldingsReadCounter();
        await using var db = fixture.CreateDbContext(options => options.AddInterceptors(counter));

        var result = await HoldingsComparisonCoverage.History(
            new InstitutionalHoldingRepository(db),
            stock,
            dates,
            []
        );

        result.Values.Should().OnlyContain(status => status.ExcludedFilers.Count == 1);
        result
            .Values.SelectMany(status => status.ExcludedFilers)
            .Should()
            .OnlyContain(filer => filer.Name == "Lapsing filer" && filer.Shares == 100);
        result[dates[1]].ExcludedPreviousShares.Should().Be(100);
        result[dates[2]].ExcludedCurrentShares.Should().Be(100);
        result
            .Values.Select(status => status.LikeForLike)
            .Should()
            .AllBeEquivalentTo(((long, long)?)(100L, 100L));
        counter
            .Reads.Should()
            .Be(
                3,
                "presence with shares, missing-side evidence and names each use one batched read"
            );
    }

    [Fact]
    public async Task History_MarksAMissingFilerWhoseQuarterIsWithheldUnderConfidentialTreatment()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "COVER",
            Name: "Coverage subject"
        );
        var stable = new InstitutionalHolder { Cik = "1", Name = "Stable filer" };
        var withholding = new InstitutionalHolder { Cik = "2", Name = "Withholding filer" };
        var pending = new InstitutionalHolder { Cik = "3", Name = "Pending filer" };
        var dates = Enumerable
            .Range(0, 3)
            .Select(i => new DateOnly(2026, 1, 1).AddMonths(i * 3).AddDays(-1))
            .ToArray();
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, stable, withholding, pending);
            foreach (var date in dates)
                seed.Add(Holding(stock, stable, date));
            // Both hold the stock around a quarter with no stored 13F of theirs.
            foreach (var holder in new[] { withholding, pending })
            {
                seed.Add(Holding(stock, holder, dates[0], shares: 700));
                seed.Add(Holding(stock, holder, dates[2], shares: 700));
            }
            seed.Add(ConfidentialFiling(withholding, dates[1], disclosedPositions: 0));
            // A report that still discloses positions has rows to import; none stored is a gap.
            seed.Add(ConfidentialFiling(pending, dates[1], disclosedPositions: 40));
            await seed.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext();

        var result = await HoldingsComparisonCoverage.History(
            new InstitutionalHoldingRepository(db),
            stock,
            dates,
            []
        );

        var withheld = result[dates[1]].WithheldFilers.Should().ContainSingle().Subject;
        withheld.Name.Should().Be("Withholding filer");
        withheld.Cik.Should().Be("2");
        withheld.Shares.Should().Be(700, "the shares it last disclosed, for the quarter before");
        result[dates[1]].Note.Should().Contain("withheld under confidential treatment");
        result[dates[2]]
            .WithheldFilers.Should()
            .BeEmpty("it reported the newer quarter; only its previous one is withheld");
        result[dates[2]]
            .ConfidentialFilers.Should()
            .Equal([withholding.Id], "the comparison still misses its previous quarter");
    }

    private static Form13FConfidentialFiling ConfidentialFiling(
        InstitutionalHolder holder,
        DateOnly reportDate,
        int disclosedPositions
    ) =>
        new()
        {
            AccessionNumber = Guid.NewGuid().ToString("N"),
            FilerCik = holder.Cik,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(40),
            DeclaredEntryTotal = 1500,
            DisclosedPositionCount = disclosedPositions,
        };

    [Fact]
    public async Task History_ComparesAFilerSplitAcrossNewCiksAsOneHolder()
    {
        // Shaped on Vanguard Group, which several new filers continue.
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "COVER",
            Name: "Coverage subject"
        );
        var stable = new InstitutionalHolder { Cik = "1", Name = "Stable filer" };
        var group = new InstitutionalHolder { Cik = "102909", Name = "Group filer" };
        var fiduciary = new InstitutionalHolder { Cik = "933478", Name = "Fiduciary filer" };
        var advisers = new InstitutionalHolder { Cik = "947529", Name = "Advisers filer" };
        var prior = new DateOnly(2025, 9, 30);
        var current = new DateOnly(2025, 12, 31);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, stable, group, fiduciary, advisers);
            seed.Add(Holding(stock, stable, prior));
            seed.Add(Holding(stock, stable, current, shares: 110));
            seed.Add(Holding(stock, group, prior, shares: 900));
            seed.Add(Holding(stock, fiduciary, current, shares: 500));
            seed.Add(Holding(stock, advisers, current, shares: 450));
            seed.AddRange(
                Succession(group, fiduciary, prior, current),
                Succession(group, advisers, prior, current)
            );
            await seed.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext();

        var result = await HoldingsComparisonCoverage.History(
            new InstitutionalHoldingRepository(db),
            stock,
            [prior, current],
            []
        );

        result[current].MissingPreviousFilers.Should().BeEmpty();
        result[current].MissingCurrentFilers.Should().BeEmpty();
        result[current].LikeForLike.Should().Be((1060L, 1000L));
        result[current]
            .Note.Should()
            .Be(
                "Compared across CIK changes: Group filer continues as 2 filers, not ranked one by one."
            );
    }

    private static HolderSuccession Succession(
        InstitutionalHolder predecessor,
        InstitutionalHolder successor,
        DateOnly last,
        DateOnly first
    ) =>
        new()
        {
            PredecessorHolderId = predecessor.Id,
            SuccessorHolderId = successor.Id,
            PredecessorLastReportDate = last,
            SuccessorFirstReportDate = first,
            Basis = HolderSuccessionBasis.NoticeReportedBy,
            PredecessorGroupValue = 1_000_000,
            SuccessorGroupValue = 1_050_000,
            DerivationVersion = 1,
        };

    [Fact]
    public async Task History_SumsEveryListedRowOfAFilerOnItsSide()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "COVER",
            Name: "Coverage subject"
        );
        var stable = new InstitutionalHolder { Cik = "1", Name = "Stable filer" };
        var lapsed = new InstitutionalHolder { Cik = "2", Name = "Lapsed filer" };
        var prior = new DateOnly(2025, 9, 30);
        var current = new DateOnly(2025, 12, 31);
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, stable, lapsed);
            seed.Add(Holding(stock, stable, prior));
            seed.Add(Holding(stock, stable, current, shares: 120));
            // Primary rows stored with and without the label, plus a sibling class.
            seed.Add(Holding(stock, lapsed, prior, shares: 40));
            seed.Add(Holding(stock, lapsed, prior, shares: 60, listedTicker: "COVER"));
            seed.Add(Holding(stock, lapsed, prior, shares: 7, listedTicker: "COVER.B"));
            await seed.SaveChangesAsync();
        }
        await using var db = fixture.CreateDbContext();

        var result = await HoldingsComparisonCoverage.History(
            new InstitutionalHoldingRepository(db),
            stock,
            [prior, current],
            []
        );

        result[current].ExcludedPreviousShares.Should().Be(107);
        result[current].LikeForLike.Should().Be((120L, 100L));
        result[current].Note.Should().Contain("Lapsed filer 107");
    }

    private static InstitutionalHolding Holding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly date,
        long shares = 100,
        string listedTicker = null
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = date,
            FilingDate = date.AddDays(40),
            FilingType = FilingType.Form13F,
            Shares = shares,
            Value = shares * 100,
            ListedTicker = listedTicker,
            AccessionNumber = Guid.NewGuid().ToString("N"),
            ShareType = ShareType.Shares,
        };
}
