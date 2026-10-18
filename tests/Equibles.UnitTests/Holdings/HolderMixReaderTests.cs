using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

public class HolderMixReaderTests
{
    private static readonly DateOnly Prior = new(2025, 9, 30);
    private static readonly DateOnly Current = new(2025, 12, 31);

    [Fact]
    public async Task Read_TotalsMatchTheOwnershipHistoryLikeForLike()
    {
        await using var db = NewDb();
        var (stock, other) = Stocks();
        var stable = Holder("Stable", "100");
        var gone = Holder("Gone", "200");
        var newcomer = Holder("Newcomer", "300");
        var opener = Holder("Opener", "400");
        db.AddRange(stock, other, stable, gone, newcomer, opener);
        db.AddRange(
            Holding(stock, stable, Prior, 100),
            Holding(stock, stable, Current, 220),
            Holding(stock, gone, Prior, 900),
            Holding(stock, newcomer, Current, 50),
            // Opener filed a 13F last quarter without this stock, so its position is new.
            Holding(other, opener, Prior, 10),
            Holding(stock, opener, Current, 30)
        );
        await db.SaveChangesAsync();
        StockSplit[] splits =
        [
            new()
            {
                EquityIssuerId = stock.Id,
                PriceSeriesTicker = "AAPL",
                EffectiveDate = new DateOnly(2025, 11, 14),
                Numerator = 2,
                Denominator = 1,
            },
        ];

        var mix = (await Reader(db).Read(stock, [Prior, Current], splits)).Single();
        var history = await HoldingsComparisonCoverage.History(
            new(db),
            stock,
            [Prior, Current],
            splits
        );

        var expected = history[Current];
        (mix.Total.LikeForLikeCurrent.Value, mix.Total.LikeForLikePrevious.Value)
            .Should()
            .Be(expected.LikeForLike.Value);
        mix.Total.ExcludedCurrentShares.Should().Be(expected.ExcludedCurrentShares);
        mix.Total.ExcludedPreviousShares.Should().Be(expected.ExcludedPreviousShares);
        mix.Total.Shares.Should().Be(220 + 50 + 30);
        mix.Total.NewFilers.Should().Be(1);
        mix.Comparison.MissingPreviousFilers.Should().BeEquivalentTo([newcomer.Id]);
        mix.Comparison.MissingCurrentFilers.Should().BeEquivalentTo([gone.Id]);
    }

    [Fact]
    public async Task Read_KeepsOptionsApartAndPrincipalOut()
    {
        await using var db = NewDb();
        var (stock, other) = Stocks();
        var holder = Holder("Options Desk", "100");
        db.AddRange(stock, other, holder);
        db.AddRange(
            Holding(stock, holder, Prior, 500, ShareType.Principal),
            Holding(stock, holder, Current, 100),
            Holding(stock, holder, Current, 40, option: OptionType.Call),
            Holding(stock, holder, Current, 25, option: OptionType.Put),
            Holding(stock, holder, Current, 700, ShareType.Principal)
        );
        await db.SaveChangesAsync();

        var mix = (await Reader(db).Read(stock, [Prior, Current], [])).Single();

        mix.Total.Shares.Should().Be(100);
        mix.Total.CallShares.Should().Be(40);
        mix.Total.PutShares.Should().Be(25);
        // A principal row still proves the filer held something last quarter, so its common
        // position is new rather than excluded.
        mix.Total.LikeForLikeCurrent.Should().Be(100);
        mix.Total.LikeForLikePrevious.Should().Be(0);
        mix.Total.NewFilers.Should().Be(1);
        mix.Total.ExcludedCurrentShares.Should().Be(0);
    }

    [Fact]
    public async Task Read_BucketsByProfileAndTheNewestMeasuredQuarterOnOrBefore()
    {
        await using var db = NewDb();
        var (stock, other) = Stocks();
        var bank = Holder("Bank", "100");
        var unprofiled = Holder("Unprofiled", "200");
        db.AddRange(stock, other, bank, unprofiled);
        db.AddRange(
            new InstitutionalHolderProfile
            {
                InstitutionalHolderId = bank.Id,
                EntityType = HolderEntityType.Bank,
            },
            // Only the prior quarter is measured, so the current quarter reads it.
            new HolderQuarterlyMetrics
            {
                InstitutionalHolderId = bank.Id,
                ReportDate = Prior,
                StyleBand = HolderStyleBand.IndexLike,
                EquityAum = 2_000_000_000,
            },
            new HolderQuarterlyMetrics
            {
                InstitutionalHolderId = bank.Id,
                ReportDate = new DateOnly(2026, 3, 31),
                StyleBand = HolderStyleBand.Concentrated,
            },
            Holding(stock, bank, Prior, 100),
            Holding(stock, bank, Current, 300),
            Holding(stock, unprofiled, Prior, 50),
            Holding(stock, unprofiled, Current, 100)
        );
        await db.SaveChangesAsync();

        var mix = (await Reader(db).Read(stock, [Prior, Current], [])).Single();

        mix.MeasuredQuarter.Should().Be(Prior);
        var types = mix.Dimensions[HolderMixDimension.EntityType].ToDictionary(b => b.Token);
        types["bank"].Shares.Should().Be(300);
        types["unk"].Shares.Should().Be(100);
        var styles = mix.Dimensions[HolderMixDimension.Style].ToDictionary(b => b.Token);
        styles["idx"].LikeForLikeCurrent.Should().Be(300);
        styles["idx"].LikeForLikePrevious.Should().Be(100);
        styles["none"].Shares.Should().Be(100);
        styles.Should().NotContainKey("conc");
        mix.Dimensions[HolderMixDimension.BookSize]
            .Single(b => b.Token == "1to10b")
            .Shares.Should()
            .Be(300);
    }

    [Fact]
    public async Task Read_PutsAPredecessorsPositionInItsSuccessorsBucketWhenTheSuccessorSoldOut()
    {
        await using var db = NewDb();
        var (stock, other) = Stocks();
        var predecessor = Holder("Old CIK", "100");
        var successor = Holder("New CIK", "200");
        db.AddRange(stock, other, predecessor, successor);
        db.AddRange(
            new InstitutionalHolderProfile
            {
                InstitutionalHolderId = successor.Id,
                EntityType = HolderEntityType.Bank,
            },
            Metrics(successor, Current, HolderStyleBand.Concentrated),
            Holding(stock, predecessor, Prior, 1_000),
            // The successor files its first 13F without this stock.
            Holding(other, successor, Current, 1_050),
            new HolderSuccession
            {
                PredecessorHolderId = predecessor.Id,
                SuccessorHolderId = successor.Id,
                PredecessorLastReportDate = Prior,
                SuccessorFirstReportDate = Current,
                Basis = HolderSuccessionBasis.NoticeReportedBy,
                PredecessorGroupValue = 1_000,
                SuccessorGroupValue = 1_050,
                DerivationVersion = 1,
            }
        );
        await db.SaveChangesAsync();

        var mix = (await Reader(db).Read(stock, [Prior, Current], [])).Single();

        var types = mix.Dimensions[HolderMixDimension.EntityType].ToDictionary(b => b.Token);
        types["bank"].LikeForLikePrevious.Should().Be(1_000);
        types["bank"].ExitedFilers.Should().Be(1);
        types.Should().NotContainKey("unk");
        mix.Dimensions[HolderMixDimension.Style].Single().Token.Should().Be("conc");
        mix.Total.ExcludedPreviousShares.Should().Be(0);
    }

    [Fact]
    public async Task Read_LeavesSchedule13DAnd13GStakesOut()
    {
        await using var db = NewDb();
        var (stock, other) = Stocks();
        var holder = Holder("Activist", "100");
        db.AddRange(stock, other, holder);
        var stake = Holding(stock, holder, Current, 9_000);
        stake.FilingType = FilingType.Schedule13D;
        db.AddRange(
            Holding(stock, holder, Prior, 100),
            Holding(stock, holder, Current, 100),
            stake
        );
        await db.SaveChangesAsync();

        var mix = (await Reader(db).Read(stock, [Prior, Current], [])).Single();

        mix.Total.Shares.Should().Be(100);
        mix.Total.LikeForLikeCurrent.Should().Be(100);
    }

    [Fact]
    public async Task GetHolderMix_ShowsOneColumnPerQuarterOldestFirst()
    {
        await using var db = NewDb();
        var (stock, other) = Stocks();
        var holder = Holder("Steady", "100");
        db.AddRange(stock, other, holder);
        db.AddRange(
            Holding(stock, holder, new DateOnly(2025, 6, 30), 100),
            Holding(stock, holder, Prior, 100),
            Holding(stock, holder, Current, 150)
        );
        await db.SaveChangesAsync();

        var output = await Tools(db).GetHolderMix("AAPL", maxPeriods: 2);

        output
            .Should()
            .Contain(
                "| Holder type | 2025-09-30 | 2025-12-31 | Chg (like-for-like) | New | Exited | Filers |"
            );
        output.Should().Contain("| Unknown | 100.0% | 100.0% | +50.0% | 0 | 0 | 1 |");
        output.Should().Contain("_Published values normally use report-date closing prices");
    }

    [Fact]
    public async Task GetHolderMix_RendersEachBucketsShareChangeAndTheActiveHolders()
    {
        await using var db = NewDb();
        var (stock, other) = Stocks();
        var index = Holder("Index Fund", "100");
        var picker = Holder("Stock Picker", "200");
        db.AddRange(stock, other, index, picker);
        db.AddRange(
            new InstitutionalHolderProfile
            {
                InstitutionalHolderId = index.Id,
                EntityType = HolderEntityType.RegisteredAdviser,
            },
            new InstitutionalHolderProfile
            {
                InstitutionalHolderId = picker.Id,
                EntityType = HolderEntityType.Bank,
            },
            Metrics(index, Current, HolderStyleBand.IndexLike),
            Metrics(picker, Current, HolderStyleBand.Concentrated),
            Holding(stock, index, Prior, 600),
            Holding(stock, index, Current, 600),
            Holding(stock, picker, Prior, 200),
            Holding(stock, picker, Current, 400)
        );
        await db.SaveChangesAsync();

        var byType = await Tools(db).GetHolderMix("AAPL", maxPeriods: 1);
        var byStyle = await Tools(db).GetHolderMix("AAPL", "STYLE", 1);
        var refused = await Tools(db).GetHolderMix("AAPL", "sector");

        byType
            .Should()
            .Contain("| Holder type | 2025-12-31 | Chg (like-for-like) | New | Exited | Filers |");
        byType.Should().Contain("| Registered adviser | 60.0% | 0.0% | 0 | 0 | 1 |");
        byType.Should().Contain("| Bank | 40.0% | +100.0% | 0 | 0 | 1 |");
        byType.Should().Contain("| All holders | 100.0% | +25.0% | 0 | 0 | 2 |");
        byType
            .Should()
            .Contain(
                "Active holders (books measured systematic-like, concentrated or diversified active in 2025-12-31): +100.0% like-for-like, 200 to 400 shares; index-like books 0.0%."
            );
        byStyle.Should().Contain("| Concentrated | 40.0% | +100.0% | 0 | 0 | 1 |");
        byStyle.Should().Contain("Measured quarter per column: 2025-12-31 reads 2025-12-31.");
        refused
            .Should()
            .Be(
                "Unknown dimension 'sector'. Use one of: type, style, turnover, horizon, aum, client, pf, country."
            );
    }

    private static (EquityIssuer Stock, EquityIssuer Other) Stocks() =>
        (
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "AAPL",
                Name: "Apple",
                Cik: "320193"
            ),
            Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: "MSFT",
                Name: "Microsoft",
                Cik: "789019"
            )
        );

    private static InstitutionalHolder Holder(string name, string cik) =>
        new() { Name = name, Cik = cik };

    private static HolderQuarterlyMetrics Metrics(
        InstitutionalHolder holder,
        DateOnly date,
        HolderStyleBand band
    ) =>
        new()
        {
            InstitutionalHolderId = holder.Id,
            ReportDate = date,
            StyleBand = band,
        };

    private static HolderMixReader Reader(EquiblesFinancialDbContext db) =>
        new(
            new(db),
            new(db),
            new(db),
            new StockCombinedQuarterService(new(db), new(db)),
            HoldingsCorpusCoverage.Default
        );

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
        long shares,
        ShareType shareType = ShareType.Shares,
        OptionType? option = null
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
            ShareType = shareType,
            OptionType = option,
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
