using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Finra.Data.Models;
using Equibles.Finra.HostedService.Services;
using Equibles.Finra.Repositories;
using Equibles.Integrations.Finra.Contracts;
using Equibles.Integrations.Finra.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

/// <summary>
/// <see cref="ShortInterestImportService.Import"/> (the single largest method
/// gap) was almost entirely uncovered. These drive the full discovery →
/// per-date missing-stock diff → bulk fetch → live-id-validated batch persist
/// pipeline end-to-end, plus the settlement-date discovery catch arm.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ShortInterestImportServicePipelineTests : ParadeDbMcpTestBase
{
    public ShortInterestImportServicePipelineTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private EquityIssuer _stock;

    private async Task SeedStock()
    {
        _stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0000000888",
            Ticker: "TESTI",
            Name: "Short Interest Test Inc."
        );
        DbContext.Add(_stock);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    private ShortInterestImportService BuildService(IFinraClient finraClient)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquityIssuerRepository), new EquityIssuerRepository(DbContext)),
            (typeof(EquityListingRepository), new EquityListingRepository(DbContext)),
            (typeof(ShortInterestRepository), new ShortInterestRepository(DbContext))
        );
        return new ShortInterestImportService(
            scopeFactory,
            Substitute.For<ILogger<ShortInterestImportService>>(),
            finraClient,
            new TickerMapService(scopeFactory),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(
                new WorkerOptions { TickersToSync = [], MinSyncDate = DateTime.UtcNow.AddDays(-30) }
            )
        );
    }

    [Fact]
    public async Task Import_RecentCompleteDate_UpdatesCorrectionWithoutDuplicatingIdentity()
    {
        await SeedStock();
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-14);
        var original = new ShortInterest
        {
            EquityListingId = _stock.Presentation.Listing.Id,
            ListedTicker = "OLDI",
            SettlementDate = date,
            CurrentShortPosition = 100,
            PreviousShortPosition = 90,
            ChangeInShortPosition = 10,
            AverageDailyVolume = 50,
            DaysToCover = 2,
        };
        DbContext.Add(original);
        await DbContext.SaveChangesAsync();
        var originalId = original.Id;
        var creationTime = original.CreationTime;
        DbContext.ChangeTracker.Clear();
        var client = Substitute.For<IFinraClient>();
        client
            .GetShortInterestSettlementDatesAfter(Arg.Any<DateOnly>())
            .Returns(new List<DateOnly>());
        client
            .GetShortInterest(date)
            .Returns([
                new ShortInterestRecord
                {
                    Symbol = "TESTI",
                    CurrentShortPosition = 200,
                    PreviousShortPosition = 120,
                    ChangeInShortPosition = 80,
                    AverageDailyVolume = 80,
                    DaysToCover = 2.5m,
                },
            ]);

        await BuildService(client).Import(CancellationToken.None);
        await BuildService(client).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var row = await verify.Set<ShortInterest>().SingleAsync();
        row.Id.Should().Be(originalId);
        row.ListedTicker.Should().Be("OLDI", "corrections preserve the original ticker evidence");
        row.CreationTime.Should().BeCloseTo(creationTime, TimeSpan.FromMilliseconds(1));
        row.CurrentShortPosition.Should().Be(200);
        row.PreviousShortPosition.Should().Be(120);
        row.ChangeInShortPosition.Should().Be(80);
        row.AverageDailyVolume.Should().Be(80);
        row.DaysToCover.Should().Be(2.5m);
    }

    [Fact]
    public async Task Import_NewDates_RequestsNewestFirst()
    {
        await SeedStock();
        var newer = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var older = newer.AddDays(-14);
        var client = Substitute.For<IFinraClient>();
        client.GetShortInterestSettlementDates().Returns([older, newer]);
        var requested = new List<DateOnly>();
        client
            .GetShortInterest(Arg.Any<DateOnly>())
            .Returns(call =>
            {
                requested.Add(call.Arg<DateOnly>());
                return new List<ShortInterestRecord>();
            });

        await BuildService(client).Import(CancellationToken.None);

        requested.Should().Equal(newer, older);
    }

    [Fact]
    public async Task Import_NewSettlementDateWithMissingStock_BulkFetchesAndPersists()
    {
        await SeedStock();
        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

        var finraClient = Substitute.For<IFinraClient>();
        finraClient.GetShortInterestSettlementDates().Returns([settlementDate]);
        finraClient
            .GetShortInterest(settlementDate)
            .Returns(
                new List<ShortInterestRecord>
                {
                    new()
                    {
                        Symbol = "TESTI",
                        CurrentShortPosition = 500_000,
                        PreviousShortPosition = 400_000,
                        ChangeInShortPosition = 100_000,
                        AverageDailyVolume = 1_000_000,
                        DaysToCover = 0.5m,
                    },
                    // Unmatched symbol → filtered out by the tickerMap guard.
                    new() { Symbol = "NOPE", CurrentShortPosition = 1 },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var rows = await verify
            .Set<ShortInterest>()
            .AsNoTracking()
            .Where(s =>
                s.Listing.Security.EquityIssuerId == _stock.Id && s.SettlementDate == settlementDate
            )
            .ToListAsync();
        rows.Should().ContainSingle("the tracked stock's short interest must be persisted");
        rows[0].CurrentShortPosition.Should().Be(500_000);
        rows[0].DaysToCover.Should().Be(0.5m);
    }

    [Fact]
    public async Task Import_CaseVariantSymbols_PersistsEachSecurity()
    {
        EquityIssuer common = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0000000889",
            Ticker: "TPC",
            Name: "Tutor Perini Corporation"
        );
        EquityIssuer preferred = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0000000890",
            Ticker: "TpC",
            Name: "Tutor Perini Preferred"
        );
        DbContext.AddRange(common, preferred);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var finraClient = Substitute.For<IFinraClient>();
        finraClient.GetShortInterestSettlementDates().Returns([settlementDate]);
        finraClient
            .GetShortInterest(settlementDate)
            .Returns(
                new List<ShortInterestRecord>
                {
                    new() { Symbol = "TPC", CurrentShortPosition = 100 },
                    new() { Symbol = "TpC", CurrentShortPosition = 200 },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var rows = await verify
            .Set<ShortInterest>()
            .AsNoTracking()
            .Where(s => s.SettlementDate == settlementDate)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Single(row => row.Listing.Security.EquityIssuerId == common.Id)
            .CurrentShortPosition.Should()
            .Be(100);
        rows.Single(row => row.Listing.Security.EquityIssuerId == preferred.Id)
            .CurrentShortPosition.Should()
            .Be(200);
    }

    [Fact]
    public async Task Import_CaseVariantCompressedClassSymbols_PersistsEachSecurity()
    {
        EquityIssuer commonClass = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0000000891",
            Ticker: "TPC-A",
            Name: "Tutor Perini Class A"
        );
        EquityIssuer preferredClass = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: "0000000892",
            Ticker: "TpC-A",
            Name: "Tutor Perini Preferred Class A"
        );
        DbContext.AddRange(commonClass, preferredClass);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var settlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var finraClient = Substitute.For<IFinraClient>();
        finraClient.GetShortInterestSettlementDates().Returns([settlementDate]);
        finraClient
            .GetShortInterest(settlementDate)
            .Returns(
                new List<ShortInterestRecord>
                {
                    new() { Symbol = "TPCA", CurrentShortPosition = 300 },
                    new() { Symbol = "TpCA", CurrentShortPosition = 400 },
                }
            );

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var rows = await verify
            .Set<ShortInterest>()
            .AsNoTracking()
            .Where(s => s.SettlementDate == settlementDate)
            .ToListAsync();
        rows.Should().HaveCount(2);
        rows.Single(row => row.Listing.Security.EquityIssuerId == commonClass.Id)
            .CurrentShortPosition.Should()
            .Be(300);
        rows.Single(row => row.Listing.Security.EquityIssuerId == preferredClass.Id)
            .CurrentShortPosition.Should()
            .Be(400);
    }

    [Fact]
    public async Task Import_SettlementDateDiscoveryThrows_ReportsErrorAndReturns()
    {
        await SeedStock();

        var finraClient = Substitute.For<IFinraClient>();
        finraClient
            .GetShortInterestSettlementDates()
            .Returns<List<DateOnly>>(_ => throw new HttpRequestException("FINRA discovery down"));

        await BuildService(finraClient).Import(CancellationToken.None);

        await using var verify = Fixture.CreateDbContext();
        var any = await verify.Set<ShortInterest>().AsNoTracking().AnyAsync();
        any.Should().BeFalse("discovery failed before any date was processed");
    }
}
