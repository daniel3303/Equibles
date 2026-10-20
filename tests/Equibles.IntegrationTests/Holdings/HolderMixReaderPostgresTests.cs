using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;

namespace Equibles.IntegrationTests.Holdings;

[Collection(ParadeDbCollection.Name)]
public class HolderMixReaderPostgresTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private static readonly DateOnly Prior = new(2025, 9, 30);
    private static readonly DateOnly Current = new(2025, 12, 31);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Read_TranslatesOnPostgresAndMatchesTheOwnershipHistory()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MIX",
            Name: "Mix subject"
        );
        EquityIssuer other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHER",
            Name: "Other issuer"
        );
        var index = new InstitutionalHolder
        {
            Cik = "1",
            Name = "Index filer",
            StateOrCountry = "X0",
        };
        var picker = new InstitutionalHolder
        {
            Cik = "2",
            Name = "Picker",
            StateOrCountry = "NY",
        };
        var opener = new InstitutionalHolder { Cik = "3", Name = "Opener" };
        var lapsing = new InstitutionalHolder { Cik = "4", Name = "Lapsing" };
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, other, index, picker, opener, lapsing);
            seed.AddRange(
                new InstitutionalHolderProfile
                {
                    InstitutionalHolderId = index.Id,
                    EntityType = HolderEntityType.RegisteredAdviser,
                },
                new HolderQuarterlyMetrics
                {
                    InstitutionalHolderId = index.Id,
                    ReportDate = Current,
                    StyleBand = HolderStyleBand.IndexLike,
                    EquityAum = 200_000_000_000,
                },
                new HolderQuarterlyMetrics
                {
                    InstitutionalHolderId = picker.Id,
                    ReportDate = Current,
                    StyleBand = HolderStyleBand.Concentrated,
                    EquityAum = 3_000_000_000,
                },
                Holding(stock, index, Prior, 1_000),
                Holding(stock, index, Current, 1_200),
                Holding(stock, picker, Prior, 300),
                Holding(stock, picker, Current, 100),
                Holding(stock, picker, Current, 50, OptionType.Put),
                Holding(other, opener, Prior, 10),
                Holding(stock, opener, Current, 40),
                Holding(stock, lapsing, Prior, 500)
            );
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var mix = (
            await new HolderMixReader(
                new(db),
                new(db),
                new(db),
                new StockCombinedQuarterService(new(db), new(db)),
                HoldingsCorpusCoverage.Default
            ).Read(stock, [Prior, Current], [])
        ).Single();
        var history = await HoldingsComparisonCoverage.History(
            new InstitutionalHoldingRepository(db),
            stock,
            [Prior, Current],
            []
        );

        mix.Total.Shares.Should().Be(1_200 + 100 + 40);
        mix.Total.PutShares.Should().Be(50);
        mix.Total.ExcludedPreviousShares.Should().Be(500);
        // The ownership history also sums the put leg, which the mix reports apart.
        history[Current].LikeForLike.Should().Be((1_200L + 100 + 50 + 40, 1_000L + 300));
        (mix.Total.LikeForLikeCurrent, mix.Total.LikeForLikePrevious)
            .Should()
            .Be((1_200L + 100 + 40, 1_000L + 300));
        var styles = mix.Dimensions[HolderMixDimension.Style].ToDictionary(b => b.Token);
        styles["idx"].Shares.Should().Be(1_200);
        styles["conc"].LikeForLikePrevious.Should().Be(300);
        styles["none"].NewFilers.Should().Be(1);
        var locations = mix.Dimensions[HolderMixDimension.Location].ToDictionary(b => b.Token);
        locations["nonus"].Shares.Should().Be(1_200);
        locations["us"].Shares.Should().Be(100);
        mix.Dimensions[HolderMixDimension.EntityType]
            .Single(b => b.Token == "adv")
            .Shares.Should()
            .Be(1_200);
    }

    [Fact]
    public async Task Read_BucketsEachQuarterByTheFormAdvRosterCurrentAtItsEnd()
    {
        // The adviser served pooled vehicles in September and individuals by December, so each
        // quarter reads its own roster; neither borrows today's profile.
        var earlier = new DateOnly(2025, 6, 30);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "PIT",
            Name: "Point in time"
        );
        var adviser = new InstitutionalHolder { Cik = "5", Name = "Adviser" };
        await using (var seed = fixture.CreateDbContext())
        {
            seed.AddRange(stock, adviser);
            seed.AddRange(
                new InstitutionalHolderProfile
                {
                    InstitutionalHolderId = adviser.Id,
                    EntityType = HolderEntityType.RegisteredAdviser,
                    AdviserCrd = 111,
                    ClientBase = HolderClientBase.Corporations,
                },
                Roster(new DateOnly(2025, 9, 1)),
                Roster(new DateOnly(2025, 12, 1)),
                Version(
                    new DateOnly(2025, 9, 1),
                    (FormAdvClientType.PooledInvestmentVehicles, 900),
                    (FormAdvClientType.Individuals, 100)
                ),
                Version(
                    new DateOnly(2025, 12, 1),
                    (FormAdvClientType.PooledInvestmentVehicles, 100),
                    (FormAdvClientType.Individuals, 900)
                ),
                Holding(stock, adviser, earlier, 100),
                Holding(stock, adviser, Prior, 100),
                Holding(stock, adviser, Current, 100)
            );
            await seed.SaveChangesAsync();
        }

        await using var db = fixture.CreateDbContext();
        var mix = await new HolderMixReader(
            new(db),
            new(db),
            new(db),
            new StockCombinedQuarterService(new(db), new(db)),
            HoldingsCorpusCoverage.Default
        ).Read(stock, [earlier, Prior, Current], []);

        string ClientBase(HolderMixQuarter quarter) =>
            quarter.Dimensions[HolderMixDimension.ClientBase].Single(b => b.Shares > 0).Token;
        mix.Select(quarter => (quarter.ReportDate, ClientBase(quarter)))
            .Should()
            .Equal((Prior, "pooled"), (Current, "ind"));
    }

    private static FormAdvRosterImport Roster(DateOnly reportDate) =>
        new()
        {
            Registration = FormAdvRegistration.Registered,
            ReportDate = reportDate,
            AdviserCount = 1,
            VersionedAt = DateTime.UtcNow,
        };

    private static FormAdvAdviserVersion Version(
        DateOnly reportDate,
        params (FormAdvClientType Type, long Aum)[] clientTypes
    )
    {
        var version = new FormAdvAdviserVersion
        {
            Crd = 111,
            Registration = FormAdvRegistration.Registered,
            ReportDate = reportDate,
            LastSeenReportDate = reportDate,
            ContentHash = reportDate.ToString("O"),
        };
        version.ClientTypes = clientTypes
            .Select(type => new FormAdvClientTypeAum
            {
                FormAdvAdviserVersionId = version.Id,
                ClientType = type.Type,
                RegulatoryAum = type.Aum,
            })
            .ToList();
        return version;
    }

    private static InstitutionalHolding Holding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        DateOnly date,
        long shares,
        OptionType? option = null
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
            OptionType = option,
            AccessionNumber = Guid.NewGuid().ToString("N"),
            ShareType = ShareType.Shares,
        };
}
