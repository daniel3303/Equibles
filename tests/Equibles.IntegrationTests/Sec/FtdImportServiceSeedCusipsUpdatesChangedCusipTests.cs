using System.Collections;
using System.IO.Compression;
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
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
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
/// recorded as a <see cref="EquityIssuerCusipAlias"/> so old filings keep
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
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "BBUC",
            Name: "Brookfield Business Corp",
            Cik: "1654795",
            Cusip: "11259V106"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
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
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(EquityIdentityManager))
                    .Returns(
                        new EquityIdentityManager(new EquityIssuerRepository(ctx), publishEndpoint)
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
        EquityIssuer persisted = await verify.Set<EquityIssuer>().FirstAsync(s => s.Id == stock.Id);
        persisted.Presentation.Listing.Security.Cusip.Should().Be("113006100");

        var alias = await verify.Set<EquityIssuerCusipAlias>().SingleAsync();
        alias.Cusip.Should().Be("11259V106");
        alias.EquityIssuerId.Should().Be(stock.Id);
    }

    [Fact]
    public async Task SeedCusips_CurrentCusipIsOwnAlias_PromotesAliasAndRetiresPreviousCusip()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TAP",
            Name: "Molson Coors Beverage Co",
            Cik: "24545",
            Cusip: "60871R100",
            SecondaryTickers: ["TAP-A"]
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityIssuerCusipAlias>()
                .Add(new EquityIssuerCusipAlias { EquityIssuerId = stock.Id, Cusip = "60871R209" });
            await seed.SaveChangesAsync();
        }

        var bus = Substitute.For<IBus>();
        var seeded = await InvokeSeedCusips(
            CreateSut(bus),
            BuildRecords(("TAP", "60871R209", new DateOnly(2026, 7, 29))),
            new Dictionary<string, Guid> { ["TAP"] = stock.Id }
        );

        seeded.Should().Be(1);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("60871R209");
        (await verify.Set<EquityIssuerCusipAlias>().SingleAsync()).Cusip.Should().Be("60871R100");
        await bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(change =>
                    change.PreviousCusip == "60871R100" && change.Cusip == "60871R209"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeedCusips_ConflictingLatestPrimaryCusips_AbstainsRegardlessOfOrder(
        bool reverse
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TAP",
            Name: "Molson Coors Beverage Co",
            Cik: "24545",
            Cusip: "60871R100"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(stock);
            await seed.SaveChangesAsync();
        }

        var first = ("TAP", "60871R209", new DateOnly(2026, 7, 31));
        var second = ("TAP", "60871R217", new DateOnly(2026, 7, 31));
        var records = reverse ? BuildRecords(second, first) : BuildRecords(first, second);
        var bus = Substitute.For<IBus>();

        var seeded = await InvokeSeedCusips(
            CreateSut(bus),
            records,
            new Dictionary<string, Guid> { ["TAP"] = stock.Id }
        );

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("60871R100");
        (await verify.Set<EquityIssuerCusipAlias>().AnyAsync()).Should().BeFalse();
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedCusips_ExactPrimaryListingClaim_ReassignsDisplacedCusipToProvenSibling()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BF-B",
            Name: "Brown Forman Corp",
            Cik: "14693",
            Cusip: "115637100",
            SecondaryTickers: ["BF-A"]
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "BF-B",
                        Cusip = "115637209",
                    }
                );
            await seed.SaveChangesAsync();
        }

        var bus = Substitute.For<IBus>();
        var seeded = await InvokeSeedCusips(
            CreateSut(bus),
            BuildRecords(
                ("BFB", "115637209", new DateOnly(2026, 7, 29)),
                ("BFA", "115637100", new DateOnly(2026, 7, 31))
            ),
            new Dictionary<string, Guid> { ["BF-B"] = stock.Id }
        );

        seeded.Should().Be(1);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("115637209");
        var listing = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        listing.ListedTicker.Should().Be("BF-A");
        listing.Cusip.Should().Be("115637100");
        (await verify.Set<EquityIssuerCusipAlias>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SeedCusips_ExactPrimaryListingWithoutDisplacedEvidence_LeavesDesignationsAlone()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BF-B",
            Name: "Brown Forman Corp",
            Cik: "14693",
            Cusip: "115637100",
            SecondaryTickers: ["BF-A"]
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "BF-B",
                        Cusip = "115637209",
                    }
                );
            await seed.SaveChangesAsync();
        }

        var seeded = await InvokeSeedCusips(
            CreateSut(Substitute.For<IBus>()),
            BuildRecords(("BFB", "115637209", new DateOnly(2026, 7, 29))),
            new Dictionary<string, Guid> { ["BF-B"] = stock.Id }
        );

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("115637100");
        var listing = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        listing.ListedTicker.Should().Be("BF-B");
        listing.Cusip.Should().Be("115637209");
    }

    [Fact]
    public async Task SeedCusips_ConflictingLatestSecondaryCusips_LeavesDesignationsAlone()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BF-B",
            Name: "Brown Forman Corp",
            Cik: "14693",
            Cusip: "115637100",
            SecondaryTickers: ["BF-A"]
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "BF-B",
                        Cusip = "115637209",
                    }
                );
            await seed.SaveChangesAsync();
        }

        var seeded = await InvokeSeedCusips(
            CreateSut(Substitute.For<IBus>()),
            BuildRecords(
                ("BFB", "115637209", new DateOnly(2026, 7, 29)),
                ("BFA", "115637100", new DateOnly(2026, 7, 31)),
                ("BFA", "115637118", new DateOnly(2026, 7, 31))
            ),
            new Dictionary<string, Guid> { ["BF-B"] = stock.Id }
        );

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("115637100");
        var listing = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        listing.ListedTicker.Should().Be("BF-B");
        listing.Cusip.Should().Be("115637209");
    }

    [Fact]
    public async Task SeedCusips_ManagerRejectsCaseVariantForeignClaim_DoesNotCountSeed()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "BF-B",
            Name: "Brown Forman Corp",
            Cik: "14693",
            Cusip: "11563R100",
            SecondaryTickers: ["BF-A"]
        );
        EquityIssuer foreign = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHER",
            Name: "Other Corp",
            Cik: "99999",
            Cusip: "999999999"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.AddRange(stock, foreign);
            seed.Set<EquityListingCusipEvidence>()
                .AddRange(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "BF-B",
                        Cusip = "11563R209",
                    },
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = foreign.Id,
                        ListedTicker = "OTHER-A",
                        Cusip = "11563r209",
                    }
                );
            await seed.SaveChangesAsync();
        }

        var seeded = await InvokeSeedCusips(
            CreateSut(Substitute.For<IBus>()),
            BuildRecords(
                ("BFB", "11563R209", new DateOnly(2026, 7, 29)),
                ("BFA", "11563R100", new DateOnly(2026, 7, 31))
            ),
            new Dictionary<string, Guid> { ["BF-B"] = stock.Id }
        );

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .Be("11563R100");
        (await verify.Set<EquityListingCusipEvidence>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task SeedInactiveCusips_AuthoritativeHistoricalMatch_SeedsRetainedIdentity()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "GONE",
            Name: "Formerly Listed Corp",
            Cik: "0000000042",
            Active: false,
            DelistedOn: new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt: DateTime.UtcNow
        );
        var listing = new EquityListingRetirementEvidence
        {
            EquityIssuerId = stock.Id,
            ListedTicker = stock.Presentation.Listing.Ticker,
            DelistedOn = stock.Presentation.Listing.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = stock
                .Presentation
                .Listing
                .HistoricalCusipBackfillRequestedAt,
        };
        var sweepBase =
            stock.Presentation.Listing.HistoricalCusipBackfillRequestedAt!.Value.AddMinutes(1);
        var sweepStartedAt = new DateTime(sweepBase.Ticks / 10 * 10 + 7, DateTimeKind.Utc);
        StageHistoricalCusip(listing, "123456789", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingRetirementEvidence>().Add(listing);
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
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(EquityIdentityManager))
                    .Returns(new EquityIdentityManager(new EquityIssuerRepository(ctx), bus));
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
        EquityIssuer persisted = await verify
            .Set<EquityIssuer>()
            .SingleAsync(row => row.Id == stock.Id);
        persisted.Presentation.Listing.Security.Cusip.Should().Be("123456789");
        (
            await verify
                .Set<EquityListingRetirementEvidence>()
                .SingleAsync(row => row.Id == listing.Id)
        )
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
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GONE",
            Name: "Formerly Listed Corp",
            Cik: "0000000042",
            Active: false,
            DelistedOn: new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt: requestedAt
        );
        var listing = new EquityListingRetirementEvidence
        {
            EquityIssuerId = stock.Id,
            ListedTicker = stock.Presentation.Listing.Ticker,
            DelistedOn = stock.Presentation.Listing.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(-1);
        StageHistoricalCusip(listing, "123456789", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingRetirementEvidence>().Add(listing);
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
        (await verify.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .BeNull();
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedInactiveCusips_MatchAfterCurrentDelistingCutoff_IsRejected()
    {
        var requestedAt = DateTime.UtcNow;
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "GONE",
            Name: "Formerly Listed Corp",
            Cik: "0000000042",
            Active: false,
            DelistedOn: new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt: requestedAt
        );
        var listing = new EquityListingRetirementEvidence
        {
            EquityIssuerId = stock.Id,
            ListedTicker = stock.Presentation.Listing.Ticker,
            DelistedOn = stock.Presentation.Listing.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(listing, "123456789", listing.DelistedOn.AddDays(1), sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingRetirementEvidence>().Add(listing);
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
        (await verify.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .BeNull();
        (
            await verify
                .Set<EquityListingRetirementEvidence>()
                .SingleAsync(row => row.Id == listing.Id)
        )
            .Cusip.Should()
            .BeNull();
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedInactiveCusips_DelistedSibling_RecordsExactListedCusip()
    {
        var requestedAt = DateTime.UtcNow;
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LIVE",
            Name: "Still Listed Filer",
            Cik: "0000000042",
            Cusip: "111111111"
        );
        var listing = new EquityListingRetirementEvidence
        {
            EquityIssuerId = stock.Id,
            ListedTicker = "OLD",
            DelistedOn = new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(listing, "222222222", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingRetirementEvidence>().Add(listing);
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
        var exact = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        exact.EquityIssuerId.Should().Be(stock.Id);
        exact.ListedTicker.Should().Be("OLD");
        exact.Cusip.Should().Be("222222222");
        (
            await verify
                .Set<EquityListingRetirementEvidence>()
                .SingleAsync(row => row.Id == listing.Id)
        )
            .Cusip.Should()
            .Be("222222222");
    }

    [Fact]
    public async Task SeedInactiveCusips_PrimaryCandidateClaimedBySameFilerSibling_RefusesMerge()
    {
        var requestedAt = DateTime.UtcNow;
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MAIN",
            Name: "Formerly Listed Filer",
            Cik: "0000000042",
            Active: false,
            DelistedOn: new DateOnly(2020, 6, 30)
        );
        var listing = new EquityListingRetirementEvidence
        {
            EquityIssuerId = stock.Id,
            ListedTicker = stock.Presentation.Listing.Ticker,
            DelistedOn = stock.Presentation.Listing.DelistedOn.Value,
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(listing, "222222222", listing.DelistedOn, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingRetirementEvidence>().Add(listing);
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
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
        (await verify.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .BeNull();
        (
            await verify
                .Set<EquityListingRetirementEvidence>()
                .SingleAsync(row => row.Id == listing.Id)
        )
            .HistoricalCusipBackfillAmbiguous.Should()
            .BeTrue();
    }

    [Fact]
    public async Task SeedInactiveCusips_SiblingCandidateEqualsParentPrimaryCusip_RefusesMerge()
    {
        var requestedAt = DateTime.UtcNow;
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "MAIN",
            Name: "Formerly Listed Filer",
            Cik: "0000000042",
            Cusip: "222222222"
        );
        var listing = new EquityListingRetirementEvidence
        {
            EquityIssuerId = stock.Id,
            ListedTicker = "OLD",
            DelistedOn = new DateOnly(2020, 6, 30),
            HistoricalCusipBackfillRequestedAt = requestedAt,
        };
        var sweepStartedAt = requestedAt.AddMinutes(1);
        StageHistoricalCusip(
            listing,
            stock.Presentation.Listing.Security.Cusip,
            listing.DelistedOn,
            sweepStartedAt
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingRetirementEvidence>().Add(listing);
            await seed.SaveChangesAsync();
        }

        var sut = CreateSut(Substitute.For<IBus>());
        var method = typeof(FtdImportService).GetMethod(
            "SeedInactiveCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        var matches = new Dictionary<Guid, (string Cusip, DateOnly SettlementDate)>
        {
            [listing.Id] = (stock.Presentation.Listing.Security.Cusip, listing.DelistedOn),
        };

        var seeded = await (Task<int>)
            method.Invoke(sut, [matches, sweepStartedAt, CancellationToken.None])!;

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquityListingCusipEvidence>().AnyAsync()).Should().BeFalse();
        var persisted = await verify
            .Set<EquityListingRetirementEvidence>()
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
        EquityIssuer owner = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OWNER",
            Name: "Identity Owner",
            Cik: "8000000001"
        );
        EquityIssuer historical = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OLD",
            Name: "Historical Candidate",
            Cik: "8000000002",
            Active: false,
            DelistedOn: settlementDate
        );
        var listing = new EquityListingRetirementEvidence
        {
            EquityIssuerId = historical.Id,
            ListedTicker = historical.Presentation.Listing.Ticker,
            DelistedOn = settlementDate,
        };
        StageHistoricalCusip(listing, contestedCusip, settlementDate, sweepStartedAt);
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.AddRange(owner, historical, listing);
            await seed.SaveChangesAsync();
        }

        await using var claimingContext = _fixture.CreateDbContext();
        EquityIssuerRepository claimingRepository = new EquityIssuerRepository(claimingContext);
        await using var claimTransaction = await claimingRepository.BeginCusipIdentityWrite();
        EquityIssuer claimingStock = await claimingContext
            .Set<EquityIssuer>()
            .SingleAsync(stock => stock.Id == owner.Id);
        claimingStock.Presentation.Listing.Security.Cusip = contestedCusip;
        await claimingContext.SaveChangesAsync();

        await using var finalizingContext = _fixture.CreateDbContext();
        EquityIdentityManager finalizer = new EquityIdentityManager(
            new EquityIssuerRepository(finalizingContext),
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
        (await verify.Set<EquityIssuer>().SingleAsync(stock => stock.Id == owner.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .Be(contestedCusip);
        (
            await verify
                .Set<EquityListingRetirementEvidence>()
                .SingleAsync(row => row.Id == listing.Id)
        )
            .Cusip.Should()
            .BeNull();
    }

    [Fact]
    public async Task SetCusip_ConcurrentDesignationChange_AbstainsFromStaleTransition()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TAP",
            Name: "Molson Coors Beverage Co",
            Cik: "24545",
            Cusip: "60871R100",
            SecondaryTickers: ["TAP-A"]
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Add(stock);
            seed.Set<EquityIssuerCusipAlias>()
                .Add(new EquityIssuerCusipAlias { EquityIssuerId = stock.Id, Cusip = "60871R209" });
            await seed.SaveChangesAsync();
        }

        await using var staleContext = _fixture.CreateDbContext();
        EquityIssuer staleStock = await staleContext
            .Set<EquityIssuer>()
            .SingleAsync(row => row.Id == stock.Id);
        var bus = Substitute.For<IBus>();
        EquityIdentityManager staleManager = new EquityIdentityManager(
            new EquityIssuerRepository(staleContext),
            bus
        );

        await using var writerContext = _fixture.CreateDbContext();
        EquityIssuerRepository writerRepository = new EquityIssuerRepository(writerContext);
        await using var writerTransaction = await writerRepository.BeginCusipIdentityWrite();
        EquityIssuer lockedStock = await writerRepository.GetForUpdate(stock.Id);
        lockedStock.Presentation.Listing.Security.Cusip = "60871R217";
        await writerRepository.SaveChanges();

        var staleTransition = staleManager.SetCusip(staleStock, "60871R209");
        var early = await Task.WhenAny(staleTransition, Task.Delay(TimeSpan.FromMilliseconds(250)));
        early.Should().NotBe(staleTransition, "the shared identity lock must serialize writers");

        await writerTransaction.CommitAsync();
        await staleTransition;

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .Be("60871R217");
        (await verify.Set<EquityIssuerCusipAlias>().SingleAsync()).Cusip.Should().Be("60871R209");
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SetCusip_CaseVariantForeignAliasClaim_Abstains()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TAP",
            Name: "Molson Coors Beverage Co",
            Cik: "24545",
            Cusip: "60871R100"
        );
        EquityIssuer foreign = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OTHER",
            Name: "Other Corp",
            Cik: "99999",
            Cusip: "999999999"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.AddRange(stock, foreign);
            seed.Set<EquityIssuerCusipAlias>()
                .AddRange(
                    new EquityIssuerCusipAlias { EquityIssuerId = stock.Id, Cusip = "60871r209" },
                    new EquityIssuerCusipAlias { EquityIssuerId = foreign.Id, Cusip = "60871R209" }
                );
            await seed.SaveChangesAsync();
        }

        await using var context = _fixture.CreateDbContext();
        EquityIssuer tracked = await context
            .Set<EquityIssuer>()
            .SingleAsync(row => row.Id == stock.Id);
        var bus = Substitute.For<IBus>();
        EquityIdentityManager manager = new EquityIdentityManager(
            new EquityIssuerRepository(context),
            bus
        );

        await manager.SetCusip(tracked, "60871R209");

        await using var verify = _fixture.CreateDbContext();
        (await verify.Set<EquityIssuer>().SingleAsync(row => row.Id == stock.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .Be("60871R100");
        (await verify.Set<EquityIssuerCusipAlias>().CountAsync()).Should().Be(2);
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedCusips_ResolvedCusipBelongsToAnotherStock_SkipsWithoutUpdating()
    {
        // Ticker-recycling shape: a delisted issuer's stale stock still holds
        // the freed symbol, and the FTD feed now maps that symbol to a CUSIP
        // that already identifies a different tracked stock. Adopting it would
        // leave two stocks sharing one CUSIP, so the row must be skipped.
        EquityIssuer staleStock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "TICK",
            Name: "Delisted Corp",
            Cik: "0000000001",
            Cusip: "111111111"
        );
        EquityIssuer currentOwner = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "NEWCO",
            Name: "New Owner Corp",
            Cik: "0000000002",
            Cusip: "222222222",
            Active: false
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().AddRange(staleStock, currentOwner);
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
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(EquityIdentityManager))
                    .Returns(
                        new EquityIdentityManager(new EquityIssuerRepository(ctx), publishEndpoint)
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
        EquityIssuer persistedStale = await verify
            .Set<EquityIssuer>()
            .FirstAsync(s => s.Id == staleStock.Id);
        persistedStale.Presentation.Listing.Security.Cusip.Should().Be("111111111");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeedCusips_ResolvedCusipIsHistoricalOrListingClaim_SkipsWithoutUpdating(
        bool listingClaim
    )
    {
        EquityIssuer target = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "TICK",
            Name: "Target Corp",
            Cik: "0000000001",
            Cusip: "111111111"
        );
        EquityIssuer owner = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "OWNER",
            Name: "Identity Owner Corp",
            Cik: "0000000002",
            Cusip: "333333333"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().AddRange(target, owner);
            if (listingClaim)
            {
                seed.Set<EquityListingCusipEvidence>()
                    .Add(
                        new EquityListingCusipEvidence
                        {
                            EquityIssuerId = owner.Id,
                            ListedTicker = "OWNER-A",
                            Cusip = "222222222",
                        }
                    );
            }
            else
            {
                seed.Set<EquityIssuerCusipAlias>()
                    .Add(
                        new EquityIssuerCusipAlias
                        {
                            EquityIssuerId = owner.Id,
                            Cusip = "222222222",
                        }
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
        (await verify.Set<EquityIssuer>().SingleAsync(stock => stock.Id == target.Id))
            .Presentation.Listing.Security.Cusip.Should()
            .Be("111111111");
        await bus.DidNotReceive()
            .Publish(Arg.Any<StockCusipChanged>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SeedCusips_CusipUnchanged_UpdatesNothingAndPublishesNothing()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "BBUC",
            Name: "Brookfield Business Corp",
            Cik: "1654795",
            Cusip: "113006100"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
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
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(EquityIdentityManager))
                    .Returns(
                        new EquityIdentityManager(new EquityIssuerRepository(ctx), publishEndpoint)
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
        (await verify.Set<EquityIssuerCusipAlias>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SeedCusips_CoRegisteredSiblingHoldsPresentationCusip_ReturnsItToPresentation()
    {
        var (stock, unit) = await SeedLaunchOne(sharedCoverPage: true);
        var bus = Substitute.For<IBus>();

        var seeded = await InvokeSeedCusips(
            CreateSut(bus),
            BuildRecords(("LPAA", "G5S86M100", new DateOnly(2026, 8, 14))),
            new Dictionary<string, Guid> { ["LPAA"] = stock.Id }
        );

        seeded.Should().Be(1);
        using var verify = FreshContext();
        var securities = await verify
            .Set<EquitySecurity>()
            .Where(security => security.EquityIssuerId == stock.Id)
            .ToDictionaryAsync(security => security.Id, security => security.Cusip);
        securities[stock.Presentation.Listing.EquitySecurityId].Should().Be("G5S86M100");
        securities[unit.Id].Should().BeNull();
        var evidence = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        (evidence.ListedTicker, evidence.Cusip).Should().Be(("LPAAU", "G5S86M118"));
        (await verify.Set<EquityIssuerCusipAlias>().AnyAsync()).Should().BeFalse();
        await bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == stock.Id && e.Cusip == "G5S86M100"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SeedCusips_SiblingHoldsPresentationCusipWithoutSharedCoverPage_LeavesItAlone()
    {
        var (stock, unit) = await SeedLaunchOne(sharedCoverPage: false);

        var seeded = await InvokeSeedCusips(
            CreateSut(Substitute.For<IBus>()),
            BuildRecords(("LPAA", "G5S86M100", new DateOnly(2026, 8, 14))),
            new Dictionary<string, Guid> { ["LPAA"] = stock.Id }
        );

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquitySecurity>().SingleAsync(security => security.Id == unit.Id))
            .Cusip.Should()
            .Be("G5S86M100");
        (
            await verify
                .Set<EquitySecurity>()
                .SingleAsync(security => security.Id == stock.Presentation.Listing.EquitySecurityId)
        )
            .Cusip.Should()
            .BeNull();
    }

    [Fact]
    public async Task ReconcileRetiredSiblingCusips_CoRegisteredRetiredClass_SwapsDesignations()
    {
        var stock = await SeedLibertyBroadband(sharedCoverPage: true);
        var bus = Substitute.For<IBus>();

        var reconciled = await CreateSut(bus).ReconcileRetiredSiblingCusips(CancellationToken.None);

        reconciled.Should().Be(1);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("530307305");
        var evidence = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        (evidence.ListedTicker, evidence.Cusip).Should().Be(("LBRDA", "530307107"));
        (await verify.Set<EquityIssuerCusipAlias>().AnyAsync()).Should().BeFalse();
        await bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == stock.Id
                    && e.PreviousCusip == "530307107"
                    && e.Cusip == "530307305"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    // A retired ticker never registered beside the presentation may be its renamed predecessor,
    // whose CUSIP is the same security's history rather than a sibling class.
    [Fact]
    public async Task ReconcileRetiredSiblingCusips_RetiredTickerNotCoRegistered_LeavesDesignationsAlone()
    {
        await SeedLibertyBroadband(sharedCoverPage: false);

        var reconciled = await CreateSut(Substitute.For<IBus>())
            .ReconcileRetiredSiblingCusips(CancellationToken.None);

        reconciled.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("530307107");
        var evidence = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        (evidence.ListedTicker, evidence.Cusip).Should().Be(("LBRDK", "530307305"));
    }

    [Fact]
    public async Task SeedCusips_SiblingWithoutItsOwnListedCusip_KeepsWhatItHolds()
    {
        var (stock, unit) = await SeedLaunchOne(sharedCoverPage: true, siblingHasOwnCusip: false);

        var seeded = await InvokeSeedCusips(
            CreateSut(Substitute.For<IBus>()),
            BuildRecords(("LPAA", "G5S86M100", new DateOnly(2026, 8, 14))),
            new Dictionary<string, Guid> { ["LPAA"] = stock.Id }
        );

        seeded.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquitySecurity>().SingleAsync(security => security.Id == unit.Id))
            .Cusip.Should()
            .Be("G5S86M100");
    }

    // A refused call must leave nothing pending, or the next stock's save in the same scope
    // commits the release without its checks.
    [Fact]
    public async Task SetCusip_RefusedAfterQualifyingRelease_LeavesNoPendingReleaseForTheNextWrite()
    {
        var (stock, unit) = await SeedLaunchOne(sharedCoverPage: true);
        EquityIssuer other = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "ZZZ",
            Name: "Unrelated Corp",
            Cik: "999"
        );
        EquityIssuer aliasOwner = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "YYY",
            Name: "Alias Owner Corp",
            Cik: "998"
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().AddRange(other, aliasOwner);
            seed.Set<EquityIssuerCusipAlias>()
                .Add(
                    new EquityIssuerCusipAlias
                    {
                        EquityIssuerId = aliasOwner.Id,
                        Cusip = "G5S86M100",
                    }
                );
            await seed.SaveChangesAsync();
        }
        var ctx = FreshContext();
        var repository = new EquityIssuerRepository(ctx);
        var manager = new EquityIdentityManager(repository, Substitute.For<IBus>());

        var refused = await manager.SetCusip(
            await repository.GetAll().SingleAsync(issuer => issuer.Id == stock.Id),
            "G5S86M100",
            null,
            "LPAAU"
        );
        var written = await manager.SetCusip(
            await repository.GetAll().SingleAsync(issuer => issuer.Id == other.Id),
            "123456789"
        );

        refused.Should().BeFalse();
        written.Should().BeTrue();
        using var verify = FreshContext();
        (await verify.Set<EquitySecurity>().SingleAsync(security => security.Id == unit.Id))
            .Cusip.Should()
            .Be("G5S86M100");
    }

    [Fact]
    public async Task ReconcileRetiredSiblingCusips_RunTwice_SwapsOnce()
    {
        await SeedLibertyBroadband(sharedCoverPage: true);
        var sut = CreateSut(Substitute.For<IBus>());

        (await sut.ReconcileRetiredSiblingCusips(CancellationToken.None)).Should().Be(1);
        (await sut.ReconcileRetiredSiblingCusips(CancellationToken.None)).Should().Be(0);

        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("530307305");
        var evidence = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        (evidence.ListedTicker, evidence.Cusip).Should().Be(("LBRDA", "530307107"));
    }

    // The inactive seeder flags this exact held-by-a-sibling conflict as ambiguous, so the flag
    // cannot veto the correction.
    [Fact]
    public async Task ReconcileRetiredSiblingCusips_StagedCandidateFlaggedByTheConflict_StillSwaps()
    {
        await SeedLibertyBroadband(sharedCoverPage: true, stagedAmbiguous: true);

        var reconciled = await CreateSut(Substitute.For<IBus>())
            .ReconcileRetiredSiblingCusips(CancellationToken.None);

        reconciled.Should().Be(1);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("530307305");
    }

    [Fact]
    public async Task ReconcileRetiredSiblingCusips_PresentationTickerWithTwoListedCusips_Abstains()
    {
        await SeedLibertyBroadband(sharedCoverPage: true, secondOwnCusip: "530307404");

        var reconciled = await CreateSut(Substitute.For<IBus>())
            .ReconcileRetiredSiblingCusips(CancellationToken.None);

        reconciled.Should().Be(0);
        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("530307107");
        (await verify.Set<EquityListingCusipEvidence>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task ReconcileSiblingCusipsFromArchive_ShareClassHoldsTheUnitsCusip_MovesItToTheUnits()
    {
        var stock = await SeedArcadia();
        var sec = Substitute.For<ISecEdgarClient>();
        var zip = FailsZip("20260728|G03456129|AMACU|100|ARCADIA UNITS|10.05");
        sec.DownloadStream(Arg.Any<string>()).Returns(_ => new MemoryStream(zip));
        var bus = Substitute.For<IBus>();
        var sut = CreateSut(bus, sec);

        (await sut.ReconcileSiblingCusipsFromArchive(CancellationToken.None)).Should().Be(1);
        (await sut.ReconcileSiblingCusipsFromArchive(CancellationToken.None))
            .Should()
            .Be(0, "the archive is read at most once a week");

        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .BeNull();
        var evidence = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        (evidence.ListedTicker, evidence.Cusip).Should().Be(("AMACU", "G03456129"));
        await bus.Received(1)
            .Publish(
                Arg.Is<StockCusipChanged>(e =>
                    e.CommonStockId == stock.Id && e.PreviousCusip == "G03456129" && e.Cusip == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ReassignSiblingCusip_PresentationWithItsOwnListedClaim_PromotesIt()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "FGBI",
            Name: "First Guaranty Bancshares",
            Cik: "1408534",
            Cusip: "32043P205",
            SecondaryTickers: ["FGBIP"]
        );
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityIssuer>().Add(stock);
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "FGBI",
                        Cusip = "32043P106",
                    }
                );
            await seed.SaveChangesAsync();
        }
        var ctx = FreshContext();
        var manager = new EquityIdentityManager(
            new EquityIssuerRepository(ctx),
            Substitute.For<IBus>()
        );

        (await manager.ReassignSiblingCusip(stock.Id, "32043P205", "FGBI", "FGBIP"))
            .Should()
            .BeTrue();

        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("32043P106");
        var evidence = await verify.Set<EquityListingCusipEvidence>().SingleAsync();
        (evidence.ListedTicker, evidence.Cusip).Should().Be(("FGBIP", "32043P205"));
    }

    [Fact]
    public async Task ReassignSiblingCusip_SiblingWithAListedCusipOfItsOwn_Refuses()
    {
        var stock = await SeedArcadia();
        await using (var seed = _fixture.CreateDbContext())
        {
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "AMACU",
                        Cusip = "G03456111",
                    }
                );
            await seed.SaveChangesAsync();
        }
        var ctx = FreshContext();
        var manager = new EquityIdentityManager(
            new EquityIssuerRepository(ctx),
            Substitute.For<IBus>()
        );

        (await manager.ReassignSiblingCusip(stock.Id, "G03456129", "AMAC", "AMACU"))
            .Should()
            .BeFalse();

        using var verify = FreshContext();
        (await verify.Set<EquityIssuer>().SingleAsync())
            .Presentation.Listing.Security.Cusip.Should()
            .Be("G03456129");
    }

    private async Task<EquityIssuer> SeedArcadia()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AMAC",
            Name: "Arcadia Acquisition Corp",
            Cik: "2050001",
            Cusip: "G03456129",
            SecondaryTickers: ["AMACU"]
        );
        await using var seed = _fixture.CreateDbContext();
        seed.Set<EquityIssuer>().Add(stock);
        seed.Set<IssuerSecurityRegistration>()
            .AddRange(
                Registration(stock.Id, "AMAC", "0002050001-26-000001"),
                Registration(stock.Id, "AMACU", "0002050001-26-000001")
            );
        await seed.SaveChangesAsync();
        return stock;
    }

    private static byte[] FailsZip(params string[] rows)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("cnsfails.txt").Open());
            writer.WriteLine("SETTLEMENT DATE|CUSIP|SYMBOL|QUANTITY (FAILS)|DESCRIPTION|PRICE");
            foreach (var row in rows)
                writer.WriteLine(row);
        }
        return buffer.ToArray();
    }

    private async Task<(EquityIssuer Stock, EquitySecurity Unit)> SeedLaunchOne(
        bool sharedCoverPage,
        bool siblingHasOwnCusip = true
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LPAA",
            Name: "Launch One Acquisition Corp",
            Cik: "2034266",
            SecondaryTickers: ["LPAAU"]
        );
        var unit = stock
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "LPAAU")
            .Security;
        unit.Cusip = "G5S86M100";
        await using var seed = _fixture.CreateDbContext();
        seed.Set<EquityIssuer>().Add(stock);
        if (siblingHasOwnCusip)
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "LPAAU",
                        Cusip = "G5S86M118",
                    }
                );
        seed.Set<IssuerSecurityRegistration>()
            .AddRange(
                Registration(stock.Id, "LPAA", "0001213900-26-088467"),
                Registration(
                    stock.Id,
                    "LPAAU",
                    sharedCoverPage ? "0001213900-26-088467" : "0001213900-25-000001"
                )
            );
        await seed.SaveChangesAsync();
        return (stock, unit);
    }

    private async Task<EquityIssuer> SeedLibertyBroadband(
        bool sharedCoverPage,
        bool stagedAmbiguous = false,
        string secondOwnCusip = null
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "LBRDK",
            Name: "Liberty Broadband Corp",
            Cik: "1611983",
            Cusip: "530307107",
            SecondaryTickers: ["LBRDA"]
        );
        var seriesA = stock
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "LBRDA");
        seriesA.Active = false;
        seriesA.IsDirectoryListed = false;
        seriesA.DelistedOn = new DateOnly(2026, 8, 21);
        await using var seed = _fixture.CreateDbContext();
        seed.Set<EquityIssuer>().Add(stock);
        seed.Set<EquityListingRetirementEvidence>()
            .Add(
                new EquityListingRetirementEvidence
                {
                    EquityIssuerId = stock.Id,
                    ListedTicker = "LBRDA",
                    DelistedOn = new DateOnly(2026, 8, 21),
                    HistoricalCusipBackfillCandidates = ["530307107"],
                    HistoricalCusipBackfillCandidateOn = new DateOnly(2026, 6, 4),
                    HistoricalCusipBackfillAmbiguous = stagedAmbiguous,
                }
            );
        if (secondOwnCusip != null)
            seed.Set<EquityListingCusipEvidence>()
                .Add(
                    new EquityListingCusipEvidence
                    {
                        EquityIssuerId = stock.Id,
                        ListedTicker = "LBRDK",
                        Cusip = secondOwnCusip,
                    }
                );
        seed.Set<EquityListingCusipEvidence>()
            .Add(
                new EquityListingCusipEvidence
                {
                    EquityIssuerId = stock.Id,
                    ListedTicker = "LBRDK",
                    Cusip = "530307305",
                }
            );
        seed.Set<IssuerSecurityRegistration>()
            .Add(Registration(stock.Id, "LBRDK", "0001140361-26-033932"));
        if (sharedCoverPage)
            seed.Set<IssuerSecurityRegistration>()
                .Add(Registration(stock.Id, "LBRDA", "0001140361-26-033932"));
        await seed.SaveChangesAsync();
        return stock;
    }

    private static IssuerSecurityRegistration Registration(
        Guid issuerId,
        string symbol,
        string accession
    ) =>
        new()
        {
            EquityIssuerId = issuerId,
            TradingSymbol = symbol,
            Title = "Class shares",
            AccessionNumber = accession,
            FiledDate = new DateOnly(2026, 8, 14),
        };

    private FtdImportService CreateSut(IBus bus) =>
        CreateSut(bus, Substitute.For<ISecEdgarClient>());

    private FtdImportService CreateSut(IBus bus, ISecEdgarClient secEdgarClient)
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                EquityIssuerRepository repository = new EquityIssuerRepository(ctx);
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                sp.GetService(typeof(EquityIssuerRepository)).Returns(repository);
                sp.GetService(typeof(EquityListingRepository))
                    .Returns(new EquityListingRepository(ctx));
                sp.GetService(typeof(BackfillStateRepository))
                    .Returns(new BackfillStateRepository(ctx));
                sp.GetService(typeof(EquityIdentityManager))
                    .Returns(new EquityIdentityManager(repository, bus));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions())
        );
    }

    private static IList BuildRecords(
        params (string Symbol, string Cusip, DateOnly SettlementDate)[] rows
    )
    {
        var recordType = typeof(FtdImportService).Assembly.GetType(
            "Equibles.Sec.HostedService.Models.FtdRecord"
        )!;
        var records = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType))!;
        foreach (var row in rows)
        {
            var record = Activator.CreateInstance(recordType)!;
            recordType.GetProperty("Symbol")!.SetValue(record, row.Symbol);
            recordType.GetProperty("Cusip")!.SetValue(record, row.Cusip);
            recordType.GetProperty("SettlementDate")!.SetValue(record, row.SettlementDate);
            records.Add(record);
        }
        return records;
    }

    private static async Task<int> InvokeSeedCusips(
        FtdImportService sut,
        IList records,
        Dictionary<string, Guid> tickerMap
    )
    {
        var method = typeof(FtdImportService).GetMethod(
            "SeedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        return await (Task<int>)method.Invoke(sut, [records, tickerMap, CancellationToken.None])!;
    }

    private static void StageHistoricalCusip(
        EquityListingRetirementEvidence listing,
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
