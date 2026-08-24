using System.Collections;
using System.Reflection;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.CommonStocks;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// SeedCusips historically only filled stocks whose CUSIP was still null, so an
/// issuer-level CUSIP change (BBUC's Class A conversion retired 11259V106 for
/// 113006100 in Q1 2026) was never picked up: every new 13F line referenced a
/// CUSIP nothing mapped, and the stock's holder count silently collapsed to the
/// laggard filers still using the old CUSIP. Pin the change-detection contract:
/// (1) a changed FTD CUSIP updates the stored stock, (2) the retired CUSIP is
/// recorded as a <see cref="CommonStockCusipAlias"/> so old filings keep
/// resolving, (3) StockCusipChanged is published so Holdings backfills, and
/// (4) the per-symbol CUSIP is resolved by LATEST SETTLEMENT DATE — a
/// transition file carries both CUSIPs, and neither first-row-wins nor
/// last-row-wins picks the right one.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FtdImportServiceSeedCusipsUpdatesChangedCusipTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public FtdImportServiceSeedCusipsUpdatesChangedCusipTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    [Fact]
    public async Task SeedCusips_SymbolCusipChanged_UpdatesStockRecordsAliasAndPublishesEvent()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBUC",
            Name = "Brookfield Business Corp",
            Cik = "1654795",
            Cusip = "11259V106",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var publishEndpoint = Substitute.For<IBus>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(new CommonStockRepository(ctx), publishEndpoint)
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        // Transition file: the retiring CUSIP trades on the early settlement
        // days and the replacement on the latest. Order the rows so the
        // newest-dated row sits in the middle — first-row-wins and
        // last-row-wins would both resolve the OLD CUSIP; only
        // latest-settlement-date-wins resolves the NEW one.
        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;

        void AddRecord(string cusip, DateOnly settlementDate)
        {
            var record = Activator.CreateInstance(recordType)!;
            recordType.GetProperty("Cusip")!.SetValue(record, cusip);
            recordType.GetProperty("Symbol")!.SetValue(record, "BBUC");
            recordType.GetProperty("SettlementDate")!.SetValue(record, settlementDate);
            list.Add(record);
        }

        AddRecord("11259V106", new DateOnly(2026, 3, 10));
        AddRecord("113006100", new DateOnly(2026, 3, 27));
        AddRecord("11259V106", new DateOnly(2026, 3, 5));

        var tickerMap = new Dictionary<string, Guid> { ["BBUC"] = stock.Id };

        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedCusips.Invoke(sut, [list, tickerMap, CancellationToken.None])!;

        seeded.Should().Be(1);

        await publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == stock.Id
                    && e.Ticker == "BBUC"
                    && e.Cusip == "113006100"
                    && e.PreviousCusip == "11259V106"
                ),
                Arg.Any<CancellationToken>()
            );

        using var verify = FreshContext();
        var persisted = await verify.Set<CommonStock>().FirstAsync(s => s.Id == stock.Id);
        persisted.Cusip.Should().Be("113006100");

        var alias = await verify.Set<CommonStockCusipAlias>().SingleAsync();
        alias.Cusip.Should().Be("11259V106");
        alias.CommonStockId.Should().Be(stock.Id);
    }

    [Fact]
    public async Task SeedInactiveCusips_AuthoritativeHistoricalMatch_SeedsRetainedIdentity()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "GONE",
            Name = "Formerly Listed Corp",
            Cik = "0000000042",
            Active = false,
            DelistedOn = new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt = DateTime.UtcNow,
        };
        var listing = new CommonStockDelistedListing
        {
            CommonStockId = stock.Id,
            ListedTicker = stock.Ticker,
            DelistedOn = stock.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = stock.HistoricalCusipBackfillRequestedAt,
        };
        var sweepStartedAt = stock.HistoricalCusipBackfillRequestedAt!.Value.AddMinutes(1);
        StageHistoricalCusip(listing, "123456789", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<CommonStockDelistedListing>().Add(listing);
            await seed.SaveChangesAsync();
        }

        var bus = Substitute.For<IBus>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(new CommonStockManager(new CommonStockRepository(ctx), bus));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );
        var matches = new Dictionary<Guid, (string Cusip, DateOnly SettlementDate)>
        {
            [listing.Id] = ("123456789", new DateOnly(2020, 6, 30)),
        };

        var seedInactive = typeof(FtdImportService).GetMethod(
            "SeedInactiveCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedInactive.Invoke(sut, [matches, sweepStartedAt, CancellationToken.None])!;

        seeded.Should().Be(1);
        using var verify = FreshContext();
        var persisted = await verify.Set<CommonStock>().SingleAsync(row => row.Id == stock.Id);
        persisted.Cusip.Should().Be("123456789");
        (await verify.Set<CommonStockDelistedListing>().SingleAsync(row => row.Id == listing.Id))
            .Cusip.Should()
            .Be("123456789");
        await bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(change =>
                    change.CommonStockId == stock.Id && change.Cusip == "123456789"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SeedInactiveCusips_RequestChangedAfterSweepStarted_LeavesIdentityForNextPass()
    {
        var requestedAt = DateTime.UtcNow;
        var stock = new CommonStock
        {
            Ticker = "GONE",
            Name = "Formerly Listed Corp",
            Cik = "0000000042",
            Active = false,
            DelistedOn = new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var listing = new CommonStockDelistedListing
        {
            CommonStockId = stock.Id,
            ListedTicker = stock.Ticker,
            DelistedOn = stock.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(-1);
        StageHistoricalCusip(listing, "123456789", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<CommonStockDelistedListing>().Add(listing);
            await seed.SaveChangesAsync();
        }

        var bus = Substitute.For<IBus>();
        var sut = CreateSut(bus);
        var matches = new Dictionary<Guid, (string Cusip, DateOnly SettlementDate)>
        {
            [listing.Id] = ("123456789", new DateOnly(2020, 6, 30)),
        };
        var method = typeof(FtdImportService).GetMethod(
            "SeedInactiveCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var seeded = await (Task<int>)
            method.Invoke(sut, [matches, sweepStartedAt, CancellationToken.None])!;

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<CommonStock>().SingleAsync(row => row.Id == stock.Id))
            .Cusip.Should()
            .BeNull();
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedInactiveCusips_MatchAfterCurrentDelistingCutoff_IsRejected()
    {
        var requestedAt = DateTime.UtcNow;
        var stock = new CommonStock
        {
            Ticker = "GONE",
            Name = "Formerly Listed Corp",
            Cik = "0000000042",
            Active = false,
            DelistedOn = new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var listing = new CommonStockDelistedListing
        {
            CommonStockId = stock.Id,
            ListedTicker = stock.Ticker,
            DelistedOn = stock.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(listing, "123456789", listing.DelistedOn.AddDays(1), sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<CommonStockDelistedListing>().Add(listing);
            await seed.SaveChangesAsync();
        }

        var bus = Substitute.For<IBus>();
        var sut = CreateSut(bus);
        var matches = new Dictionary<Guid, (string Cusip, DateOnly SettlementDate)>
        {
            [listing.Id] = ("123456789", listing.DelistedOn.AddDays(1)),
        };
        var method = typeof(FtdImportService).GetMethod(
            "SeedInactiveCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var seeded = await (Task<int>)
            method.Invoke(sut, [matches, sweepStartedAt, CancellationToken.None])!;

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<CommonStock>().SingleAsync(row => row.Id == stock.Id))
            .Cusip.Should()
            .BeNull();
        (await verify.Set<CommonStockDelistedListing>().SingleAsync(row => row.Id == listing.Id))
            .Cusip.Should()
            .BeNull();
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedInactiveCusips_DelistedSibling_RecordsExactListedCusip()
    {
        var requestedAt = DateTime.UtcNow;
        var stock = new CommonStock
        {
            Ticker = "LIVE",
            Name = "Still Listed Filer",
            Cik = "0000000042",
            Cusip = "111111111",
        };
        var listing = new CommonStockDelistedListing
        {
            CommonStockId = stock.Id,
            ListedTicker = "OLD",
            DelistedOn = new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(listing, "222222222", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<CommonStockDelistedListing>().Add(listing);
            await seed.SaveChangesAsync();
        }

        var bus = Substitute.For<IBus>();
        var sut = CreateSut(bus);
        var method = typeof(FtdImportService).GetMethod(
            "SeedInactiveCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var matches = new Dictionary<Guid, (string Cusip, DateOnly SettlementDate)>
        {
            [listing.Id] = ("222222222", listing.DelistedOn),
        };

        var seeded = await (Task<int>)
            method.Invoke(sut, [matches, sweepStartedAt, CancellationToken.None])!;

        seeded.Should().Be(1);
        using var verify = FreshContext();
        var exact = await verify.Set<CommonStockListedCusip>().SingleAsync();
        exact.CommonStockId.Should().Be(stock.Id);
        exact.ListedTicker.Should().Be("OLD");
        exact.Cusip.Should().Be("222222222");
        (await verify.Set<CommonStockDelistedListing>().SingleAsync(row => row.Id == listing.Id))
            .Cusip.Should()
            .Be("222222222");
    }

    [Fact]
    public async Task SeedInactiveCusips_PrimaryCandidateClaimedBySameFilerSibling_RefusesMerge()
    {
        var requestedAt = DateTime.UtcNow;
        var stock = new CommonStock
        {
            Ticker = "MAIN",
            Name = "Formerly Listed Filer",
            Cik = "0000000042",
            Active = false,
            DelistedOn = new DateOnly(2020, 6, 30),
        };
        var listing = new CommonStockDelistedListing
        {
            CommonStockId = stock.Id,
            ListedTicker = stock.Ticker,
            DelistedOn = stock.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(listing, "222222222", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<CommonStockDelistedListing>().Add(listing);
            seed.Set<CommonStockListedCusip>()
                .Add(
                    new CommonStockListedCusip
                    {
                        CommonStockId = stock.Id,
                        ListedTicker = "SIBLING",
                        Cusip = "222222222",
                    }
                );
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(Substitute.For<IBus>());
        var method = typeof(FtdImportService).GetMethod(
            "SeedInactiveCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var matches = new Dictionary<Guid, (string Cusip, DateOnly SettlementDate)>
        {
            [listing.Id] = ("222222222", listing.DelistedOn),
        };

        var seeded = await (Task<int>)
            method.Invoke(sut, [matches, sweepStartedAt, CancellationToken.None])!;

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<CommonStock>().SingleAsync(row => row.Id == stock.Id))
            .Cusip.Should()
            .BeNull();
        (await verify.Set<CommonStockDelistedListing>().SingleAsync(row => row.Id == listing.Id))
            .HistoricalCusipBackfillAmbiguous.Should()
            .BeTrue();
    }

    [Fact]
    public async Task SeedInactiveCusips_SiblingCandidateEqualsParentPrimaryCusip_RefusesMerge()
    {
        var requestedAt = DateTime.UtcNow;
        var stock = new CommonStock
        {
            Ticker = "MAIN",
            Name = "Formerly Listed Filer",
            Cik = "0000000042",
            Cusip = "222222222",
        };
        var listing = new CommonStockDelistedListing
        {
            CommonStockId = stock.Id,
            ListedTicker = "OLD",
            DelistedOn = new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(listing, stock.Cusip, listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            seed.Set<CommonStockDelistedListing>().Add(listing);
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(Substitute.For<IBus>());
        var method = typeof(FtdImportService).GetMethod(
            "SeedInactiveCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var matches = new Dictionary<Guid, (string Cusip, DateOnly SettlementDate)>
        {
            [listing.Id] = (stock.Cusip, listing.DelistedOn),
        };

        var seeded = await (Task<int>)
            method.Invoke(sut, [matches, sweepStartedAt, CancellationToken.None])!;

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<CommonStockListedCusip>().AnyAsync()).Should().BeFalse();
        var persisted = await verify
            .Set<CommonStockDelistedListing>()
            .SingleAsync(row => row.Id == listing.Id);
        persisted.Cusip.Should().BeNull();
        persisted.HistoricalCusipBackfillAmbiguous.Should().BeTrue();
    }

    [Fact]
    public async Task SeedDelistedListingCusip_ConcurrentPrimaryClaim_KeepsFirstOwner()
    {
        const string contestedCusip = "555555555";
        var sweepStartedAt = DateTime.UtcNow.AddMinutes(-1);
        var settlementDate = new DateOnly(2020, 6, 30);
        var owner = new CommonStock
        {
            Ticker = "OWNER",
            Name = "Identity Owner",
            Cik = "8000000001",
        };
        var historical = new CommonStock
        {
            Ticker = "OLD",
            Name = "Historical Candidate",
            Cik = "8000000002",
            Active = false,
            DelistedOn = settlementDate,
        };
        var listing = new CommonStockDelistedListing
        {
            CommonStockId = historical.Id,
            ListedTicker = historical.Ticker,
            DelistedOn = settlementDate,
        };
        StageHistoricalCusip(listing, contestedCusip, settlementDate, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.AddRange(owner, historical, listing);
            await seed.SaveChangesAsync();
        }

        await using var claimingContext = _fixture.CreateDbContext();
        var claimingRepository = new CommonStockRepository(claimingContext);
        await using var claimTransaction = await claimingRepository.BeginCusipIdentityWrite();
        var claimingStock = await claimingContext
            .Set<CommonStock>()
            .SingleAsync(stock => stock.Id == owner.Id);
        claimingStock.Cusip = contestedCusip;
        await claimingContext.SaveChangesAsync();

        await using var finalizingContext = _fixture.CreateDbContext();
        var finalizer = new CommonStockManager(
            new CommonStockRepository(finalizingContext),
            Substitute.For<IBus>()
        );
        var finalization = finalizer.SeedDelistedListingCusip(
            listing.Id,
            contestedCusip,
            settlementDate,
            sweepStartedAt
        );
        var early = await Task.WhenAny(finalization, Task.Delay(TimeSpan.FromMilliseconds(250)));
        early.Should().NotBe(finalization, "the shared identity lock must serialize writers");

        await claimTransaction.CommitAsync();
        (await finalization).Should().Be(DelistedListingCusipSeedResult.ClaimedByAnotherStock);

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<CommonStock>().SingleAsync(stock => stock.Id == owner.Id))
            .Cusip.Should()
            .Be(contestedCusip);
        (await verify.Set<CommonStockDelistedListing>().SingleAsync(row => row.Id == listing.Id))
            .Cusip.Should()
            .BeNull();
    }

    [Fact]
    public async Task SeedCusips_ResolvedCusipBelongsToAnotherStock_SkipsWithoutUpdating()
    {
        // Ticker-recycling shape: a delisted issuer's stale stock still holds
        // the freed symbol, and the FTD feed now maps that symbol to a CUSIP
        // that already identifies a different tracked stock. Adopting it would
        // leave two stocks sharing one CUSIP, so the row must be skipped.
        var staleStock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "TICK",
            Name = "Delisted Corp",
            Cik = "0000000001",
            Cusip = "111111111",
        };
        var currentOwner = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "NEWCO",
            Name = "New Owner Corp",
            Cik = "0000000002",
            Cusip = "222222222",
            Active = false,
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().AddRange(staleStock, currentOwner);
            await seed.SaveChangesAsync();
        }

        var publishEndpoint = Substitute.For<IBus>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(new CommonStockRepository(ctx), publishEndpoint)
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Cusip")!.SetValue(record, "222222222");
        recordType.GetProperty("Symbol")!.SetValue(record, "TICK");
        recordType.GetProperty("SettlementDate")!.SetValue(record, new DateOnly(2026, 6, 12));
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        list.Add(record);

        var tickerMap = new Dictionary<string, Guid>
        {
            ["TICK"] = staleStock.Id,
            ["NEWCO"] = currentOwner.Id,
        };

        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedCusips.Invoke(sut, [list, tickerMap, CancellationToken.None])!;

        seeded.Should().Be(0);
        await publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());

        using var verify = FreshContext();
        var persistedStale = await verify.Set<CommonStock>().FirstAsync(s => s.Id == staleStock.Id);
        persistedStale.Cusip.Should().Be("111111111");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeedCusips_ResolvedCusipIsHistoricalOrListingClaim_SkipsWithoutUpdating(
        bool listingClaim
    )
    {
        var target = new CommonStock
        {
            Ticker = "TICK",
            Name = "Target Corp",
            Cik = "0000000001",
            Cusip = "111111111",
        };
        var owner = new CommonStock
        {
            Ticker = "OWNER",
            Name = "Identity Owner Corp",
            Cik = "0000000002",
            Cusip = "333333333",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().AddRange(target, owner);
            if (listingClaim)
            {
                seed.Set<CommonStockListedCusip>()
                    .Add(
                        new CommonStockListedCusip
                        {
                            CommonStockId = owner.Id,
                            ListedTicker = "OWNER-A",
                            Cusip = "222222222",
                        }
                    );
            }
            else
            {
                seed.Set<CommonStockCusipAlias>()
                    .Add(
                        new CommonStockCusipAlias { CommonStockId = owner.Id, Cusip = "222222222" }
                    );
            }
            await seed.SaveChangesAsync();
        }

        var bus = Substitute.For<IBus>();
        var sut = CreateSut(bus);
        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Cusip")!.SetValue(record, "222222222");
        recordType.GetProperty("Symbol")!.SetValue(record, "TICK");
        recordType.GetProperty("SettlementDate")!.SetValue(record, new DateOnly(2026, 6, 12));
        var records = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        records.Add(record);
        var method = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        var seeded = await (Task<int>)
            method.Invoke(
                sut,
                [
                    records,
                    new Dictionary<string, Guid> { ["TICK"] = target.Id },
                    CancellationToken.None,
                ]
            )!;

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<CommonStock>().SingleAsync(stock => stock.Id == target.Id))
            .Cusip.Should()
            .Be("111111111");
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedCusips_CusipUnchanged_UpdatesNothingAndPublishesNothing()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "BBUC",
            Name = "Brookfield Business Corp",
            Cik = "1654795",
            Cusip = "113006100",
        };
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var publishEndpoint = Substitute.For<IBus>();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(CommonStockManager))
                    .Returns(
                        new CommonStockManager(new CommonStockRepository(ctx), publishEndpoint)
                    );
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        var sut = new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );

        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var record = Activator.CreateInstance(recordType)!;
        recordType.GetProperty("Cusip")!.SetValue(record, "113006100");
        recordType.GetProperty("Symbol")!.SetValue(record, "BBUC");
        recordType.GetProperty("SettlementDate")!.SetValue(record, new DateOnly(2026, 6, 12));
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        list.Add(record);

        var tickerMap = new Dictionary<string, Guid> { ["BBUC"] = stock.Id };

        var seedCusips = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var seeded = await (Task<int>)
            seedCusips.Invoke(sut, [list, tickerMap, CancellationToken.None])!;

        seeded.Should().Be(0);
        await publishEndpoint
            .DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());

        using var verify = FreshContext();
        (await verify.Set<CommonStockCusipAlias>().AnyAsync()).Should().BeFalse();
    }

    private FtdImportService CreateSut(IBus bus)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var repository = new CommonStockRepository(ctx);
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(CommonStockRepository)).Returns(repository);
                sp.GetService(typeof(CommonStockManager))
                    .Returns(new CommonStockManager(repository, bus));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return new FtdImportService(
            scopeFactory,
            Substitute.For<ISecEdgarClient>(),
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );
    }

    private static void StageHistoricalCusip(
        CommonStockDelistedListing listing,
        string cusip,
        DateOnly settlementDate,
        DateTime sweepStartedAt
    )
    {
        listing.HistoricalCusipBackfillCandidates = [cusip];
        listing.HistoricalCusipBackfillCandidateOn = settlementDate;
        listing.HistoricalCusipBackfillSweepStartedAt = sweepStartedAt;
    }
}
