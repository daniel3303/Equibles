using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Holdings;

public class HoldingsValueRecalculatorTests : IDisposable {
    private static readonly IModuleConfiguration[] Modules = [
        new CommonStocksModuleConfiguration(),
        new HoldingsModuleConfiguration(),
    ];

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly IStockPriceProvider _priceProvider = Substitute.For<IStockPriceProvider>();
    private readonly ILogger<HoldingsValueRecalculator> _logger = Substitute.For<ILogger<HoldingsValueRecalculator>>();
    private readonly List<EquiblesDbContext> _contexts = [];

    public void Dispose() {
        foreach (var ctx in _contexts) {
            ctx.Dispose();
        }
    }

    /// <summary>
    /// Creates a new <see cref="EquiblesDbContext"/> backed by the same in-memory database,
    /// so every scope-resolved context shares the same data store.
    /// </summary>
    private EquiblesDbContext CreateSharedContext() {
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        var ctx = new EquiblesDbContext(options, Modules);
        ctx.Database.EnsureCreated();
        _contexts.Add(ctx);
        return ctx;
    }

    /// <summary>
    /// Builds an <see cref="IServiceScopeFactory"/> whose scopes resolve
    /// <see cref="EquiblesDbContext"/> from the shared in-memory database.
    /// Each call to <c>CreateScope()</c> returns a fresh context instance
    /// (mirroring real DI behaviour) that still shares the same backing store.
    /// </summary>
    private IServiceScopeFactory CreateScopeFactory() {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        scopeFactory.CreateScope().Returns(_ => {
            var ctx = CreateSharedContext();

            var sp = Substitute.For<IServiceProvider>();
            sp.GetService(typeof(EquiblesDbContext)).Returns(ctx);

            var scope = Substitute.For<IServiceScope>();
            scope.ServiceProvider.Returns(sp);
            return scope;
        });

        return scopeFactory;
    }

    private HoldingsValueRecalculator CreateRecalculator(IServiceScopeFactory scopeFactory) {
        return new HoldingsValueRecalculator(scopeFactory, _priceProvider, _logger);
    }

    private static InstitutionalHolder CreateHolder(string name = "Vanguard") {
        return new InstitutionalHolder {
            Id = Guid.NewGuid(),
            Cik = Guid.NewGuid().ToString()[..10],
            Name = name,
        };
    }

    private static InstitutionalHolding CreateHolding(
        Guid stockId,
        Guid holderId,
        DateOnly reportDate,
        long shares,
        bool valuePending,
        long value = 0,
        List<HoldingManagerEntry> managerEntries = null
    ) {
        return new InstitutionalHolding {
            Id = Guid.NewGuid(),
            CommonStockId = stockId,
            InstitutionalHolderId = holderId,
            ReportDate = reportDate,
            FilingDate = reportDate.AddDays(30),
            Shares = shares,
            Value = value,
            ValuePending = valuePending,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
            ManagerEntries = managerEntries ?? [],
        };
    }

    // ── Recalculates value based on shares * price ─────────────────────

    [Fact]
    public async Task Recalculate_PendingHoldings_SetsValueToSharesTimesPrice() {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple" };
        var holder = CreateHolder();
        var reportDate = new DateOnly(2024, 3, 31);

        var holding = CreateHolding(stock.Id, holder.Id, reportDate, shares: 1000, valuePending: true);

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        await seedContext.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), decimal> {
                [(stock.Id, reportDate)] = 150.50m,
            });

        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        // Read from a fresh context to verify persisted state
        var verifyContext = CreateSharedContext();
        var updated = await verifyContext.Set<InstitutionalHolding>()
            .FirstAsync(h => h.Id == holding.Id);

        updated.Value.Should().Be((long)(1000 * 150.50m));
        updated.ValuePending.Should().BeFalse();
    }

    [Fact]
    public async Task Recalculate_PendingHoldingWithManagerEntries_RecalculatesEntryValues() {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "TSLA", Name = "Tesla" };
        var holder = CreateHolder("BlackRock");
        var reportDate = new DateOnly(2024, 6, 30);

        var holding = CreateHolding(stock.Id, holder.Id, reportDate, shares: 5000, valuePending: true,
            managerEntries: [
                new HoldingManagerEntry { ManagerNumber = 1, ManagerName = "Fund A", Shares = 3000, Value = 0 },
                new HoldingManagerEntry { ManagerNumber = 2, ManagerName = "Fund B", Shares = 2000, Value = 0 },
            ]);

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        await seedContext.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), decimal> {
                [(stock.Id, reportDate)] = 200m,
            });

        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        var verifyContext = CreateSharedContext();
        var updated = await verifyContext.Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .FirstAsync(h => h.Id == holding.Id);

        updated.Value.Should().Be(5000 * 200);
        updated.ManagerEntries.Should().HaveCount(2);
        updated.ManagerEntries.First(e => e.ManagerName == "Fund A").Value.Should().Be(3000 * 200);
        updated.ManagerEntries.First(e => e.ManagerName == "Fund B").Value.Should().Be(2000 * 200);
    }

    // ── Skips holdings that already have a value ───────────────────────

    [Fact]
    public async Task Recalculate_HoldingsNotPending_AreNotModified() {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "MSFT", Name = "Microsoft" };
        var holder = CreateHolder();
        var reportDate = new DateOnly(2024, 3, 31);

        var alreadyValued = CreateHolding(stock.Id, holder.Id, reportDate,
            shares: 500, valuePending: false, value: 99999);

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(alreadyValued);
        await seedContext.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), decimal>());

        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        var verifyContext = CreateSharedContext();
        var unchanged = await verifyContext.Set<InstitutionalHolding>()
            .FirstAsync(h => h.Id == alreadyValued.Id);

        unchanged.Value.Should().Be(99999, "non-pending holdings should keep their original value");
        unchanged.ValuePending.Should().BeFalse();
    }

    [Fact]
    public async Task Recalculate_MixOfPendingAndNonPending_OnlyUpdatesPending() {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "GOOG", Name = "Alphabet" };
        var holder = CreateHolder();
        var reportDate = new DateOnly(2024, 3, 31);

        var pendingHolding = CreateHolding(stock.Id, holder.Id, reportDate,
            shares: 1000, valuePending: true);

        // Need a different holder for unique constraint (same stock+holder+date+shareType would collide)
        var holder2 = CreateHolder("Fidelity");
        var nonPendingHolding = CreateHolding(stock.Id, holder2.Id, reportDate,
            shares: 2000, valuePending: false, value: 77777);

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().AddRange(holder, holder2);
        seedContext.Set<InstitutionalHolding>().AddRange(pendingHolding, nonPendingHolding);
        await seedContext.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), decimal> {
                [(stock.Id, reportDate)] = 100m,
            });

        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        var verifyContext = CreateSharedContext();
        var holdings = await verifyContext.Set<InstitutionalHolding>().ToListAsync();

        var updated = holdings.First(h => h.Id == pendingHolding.Id);
        updated.Value.Should().Be(1000 * 100);
        updated.ValuePending.Should().BeFalse();

        var untouched = holdings.First(h => h.Id == nonPendingHolding.Id);
        untouched.Value.Should().Be(77777);
        untouched.ValuePending.Should().BeFalse();
    }

    // ── Handles case where no price is available ───────────────────────

    [Fact]
    public async Task Recalculate_NoPricesAvailable_LeavesHoldingsPending() {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "NVDA", Name = "NVIDIA" };
        var holder = CreateHolder();
        var reportDate = new DateOnly(2024, 3, 31);

        var holding = CreateHolding(stock.Id, holder.Id, reportDate,
            shares: 800, valuePending: true);

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        await seedContext.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), decimal>());

        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        var verifyContext = CreateSharedContext();
        var unchanged = await verifyContext.Set<InstitutionalHolding>()
            .FirstAsync(h => h.Id == holding.Id);

        unchanged.ValuePending.Should().BeTrue("holding should remain pending when no price is available");
        unchanged.Value.Should().Be(0);
    }

    [Fact]
    public async Task Recalculate_PriceAvailableForSomeStocks_OnlyRecalculatesThoseWithPrices() {
        var seedContext = CreateSharedContext();
        var stockWithPrice = new CommonStock { Id = Guid.NewGuid(), Ticker = "AAPL", Name = "Apple" };
        var stockWithoutPrice = new CommonStock { Id = Guid.NewGuid(), Ticker = "AMZN", Name = "Amazon" };
        var holder = CreateHolder();
        var reportDate = new DateOnly(2024, 3, 31);

        var holdingWithPrice = CreateHolding(stockWithPrice.Id, holder.Id, reportDate,
            shares: 1000, valuePending: true);
        var holder2 = CreateHolder("State Street");
        var holdingWithoutPrice = CreateHolding(stockWithoutPrice.Id, holder2.Id, reportDate,
            shares: 2000, valuePending: true);

        seedContext.Set<CommonStock>().AddRange(stockWithPrice, stockWithoutPrice);
        seedContext.Set<InstitutionalHolder>().AddRange(holder, holder2);
        seedContext.Set<InstitutionalHolding>().AddRange(holdingWithPrice, holdingWithoutPrice);
        await seedContext.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<(Guid, DateOnly), decimal> {
                [(stockWithPrice.Id, reportDate)] = 175m,
            });

        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        var verifyContext = CreateSharedContext();
        var holdings = await verifyContext.Set<InstitutionalHolding>().ToListAsync();

        var recalculated = holdings.First(h => h.Id == holdingWithPrice.Id);
        recalculated.Value.Should().Be(1000 * 175);
        recalculated.ValuePending.Should().BeFalse();

        var stillPending = holdings.First(h => h.Id == holdingWithoutPrice.Id);
        stillPending.ValuePending.Should().BeTrue();
        stillPending.Value.Should().Be(0);
    }

    // ── Handles empty holdings list ────────────────────────────────────

    [Fact]
    public async Task Recalculate_NoHoldingsInDatabase_ReturnsEarlyWithoutCallingPriceProvider() {
        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        await _priceProvider
            .DidNotReceive()
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recalculate_AllHoldingsAlreadyValued_ReturnsEarlyWithoutCallingPriceProvider() {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock { Id = Guid.NewGuid(), Ticker = "META", Name = "Meta" };
        var holder = CreateHolder();
        var reportDate = new DateOnly(2024, 3, 31);

        var holding = CreateHolding(stock.Id, holder.Id, reportDate,
            shares: 500, valuePending: false, value: 50000);

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        await seedContext.SaveChangesAsync();

        var scopeFactory = CreateScopeFactory();
        var recalculator = CreateRecalculator(scopeFactory);

        await recalculator.Recalculate(CancellationToken.None);

        await _priceProvider
            .DidNotReceive()
            .GetClosingPrices(Arg.Any<IEnumerable<(Guid, DateOnly)>>(), Arg.Any<CancellationToken>());
    }
}
