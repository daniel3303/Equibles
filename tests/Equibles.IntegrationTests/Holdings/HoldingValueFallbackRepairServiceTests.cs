using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the back-catalogue heal: abandoned zero-value rows get the filer's figure published (and
/// the rollups they feed re-derived), implausible derivations get reset for honest repricing, and
/// a second pass over a healed database is a no-op.
/// </summary>
public class HoldingValueFallbackRepairServiceTests : IDisposable
{
    private static readonly IModuleConfiguration[] Modules =
    [
        new CommonStocksModuleConfiguration(),
        new HoldingsModuleConfiguration(),
        new CorporateActionsModuleConfiguration(),
    ];

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly ILogger<HoldingValueFallbackRepairService> _logger = Substitute.For<
        ILogger<HoldingValueFallbackRepairService>
    >();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var ctx in _contexts)
        {
            ctx.Dispose();
        }
    }

    private EquiblesFinancialDbContext CreateSharedContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(_dbName)
            .Options;

        var ctx = new EquiblesFinancialDbContext(options, Modules);
        ctx.Database.EnsureCreated();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();

        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = CreateSharedContext();

                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);

                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });

        return scopeFactory;
    }

    private HoldingValueFallbackRepairService CreateService() => new(CreateScopeFactory(), _logger);

    private async Task<InstitutionalHolding> SeedHolding(
        long value,
        long? filedValue,
        long shares,
        bool valuePending = false,
        bool valueUnavailable = false,
        ValueSource valueSource = ValueSource.Derived,
        string accession = null,
        int valueRetryCount = 0
    )
    {
        var seedContext = CreateSharedContext();
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = Guid.NewGuid().ToString()[..4],
            Name = "Issuer",
        };
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = Guid.NewGuid().ToString()[..10],
            Name = "Filer",
        };
        var holding = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = new DateOnly(2026, 3, 31),
            FilingDate = new DateOnly(2026, 5, 10),
            Shares = shares,
            Value = value,
            FiledValue = filedValue,
            ValuePending = valuePending,
            ValueUnavailable = valueUnavailable,
            ValueSource = valueSource,
            ValueRetryCount = valueRetryCount,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession ?? Guid.NewGuid().ToString()[..20],
            ManagerEntries =
            [
                new HoldingManagerEntry
                {
                    ManagerNumber = 1,
                    ManagerName = "Leg",
                    Shares = shares,
                    Value = value,
                },
            ],
        };

        seedContext.Set<CommonStock>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        await seedContext.SaveChangesAsync();
        return holding;
    }

    private async Task<InstitutionalHolding> Reload(Guid holdingId)
    {
        var verifyContext = CreateSharedContext();
        return await verifyContext
            .Set<InstitutionalHolding>()
            .Include(h => h.ManagerEntries)
            .FirstAsync(h => h.Id == holdingId);
    }

    // ── Phase 1: stuck zeros ───────────────────────────────────────────

    [Fact]
    public async Task Repair_AbandonedZeroWithFiledValue_PublishesFiledValue()
    {
        var holding = await SeedHolding(value: 0L, filedValue: 11_917_694_981L, shares: 68_335_407);

        var repaired = await CreateService().Repair(CancellationToken.None);

        repaired.Should().Be(1);
        var updated = await Reload(holding.Id);
        updated.Value.Should().Be(11_917_694_981L);
        updated.ValueSource.Should().Be(ValueSource.Filed);
        updated.ManagerEntries.Single().Value.Should().Be(11_917_694_981L);
    }

    [Theory]
    [InlineData(true, false)] // still pending — the repricing lane owns it
    [InlineData(false, true)] // unavailable — deliberately withheld, not abandoned
    public async Task Repair_PendingOrUnavailableZeroRows_AreLeftAlone(
        bool pending,
        bool unavailable
    )
    {
        var holding = await SeedHolding(
            value: 0L,
            filedValue: 500_000L,
            shares: 1000,
            valuePending: pending,
            valueUnavailable: unavailable
        );

        await CreateService().Repair(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.Value.Should().Be(0L);
        updated.ValueSource.Should().Be(ValueSource.Derived);
    }

    [Fact]
    public async Task Repair_ZeroWithoutFiledValueAndNoRetries_IsLeftAlone()
    {
        // ValueRetryCount = 0 means the row was never on the ladder — a legitimately derived
        // zero (a sub-dollar position), not an abandoned one. No phase may touch it.
        var holding = await SeedHolding(value: 0L, filedValue: null, shares: 1000);

        var repaired = await CreateService().Repair(CancellationToken.None);

        repaired.Should().Be(0);
        var updated = await Reload(holding.Id);
        updated.Value.Should().Be(0L);
        updated.ValueUnavailable.Should().BeFalse();
    }

    // ── Phase 3: abandoned zeros with no filed figure ──────────────────

    [Fact]
    public async Task Repair_AbandonedZeroWithoutFiledValue_IsMarkedUnavailable()
    {
        // The old ladder cleared ValuePending on give-up without stamping anything, leaving
        // "unknown" indistinguishable from "worth nothing" — invisible to every surface that
        // discloses unvalued rows through the flags.
        var holding = await SeedHolding(
            value: 0L,
            filedValue: null,
            shares: 1000,
            valueRetryCount: 4
        );

        var repaired = await CreateService().Repair(CancellationToken.None);

        repaired.Should().Be(1);
        var updated = await Reload(holding.Id);
        updated.Value.Should().Be(0L);
        updated.ValueUnavailable.Should().BeTrue();
        updated.ValuePending.Should().BeFalse();
    }

    // ── Phase 2: implausible derivations ───────────────────────────────

    [Fact]
    public async Task Repair_ImplausibleImpliedPrice_ResetsRowForRepricing()
    {
        // The production signature: 273,201 shares carrying $13.66T — an implied
        // $50M/share. Reset to pending so the guarded recalculator re-derives it.
        var holding = await SeedHolding(
            value: 13_660_050_000_000L,
            filedValue: 1_092_804L,
            shares: 273_201
        );

        await CreateService().Repair(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeTrue();
        updated.Value.Should().Be(0L);
        updated.ValueRetryCount.Should().Be(0);
        updated.ValueLastRetryAt.Should().BeNull();
        updated.ManagerEntries.Single().Value.Should().Be(0L);
    }

    [Fact]
    public async Task Repair_BrkAClassImpliedPrice_IsNotTouched()
    {
        // ~$800k/share is a real price (BRK-A). Must never be treated as corrupt.
        var holding = await SeedHolding(value: 80_000_000_000L, filedValue: null, shares: 100_000);

        var repaired = await CreateService().Repair(CancellationToken.None);

        repaired.Should().Be(0);
        (await Reload(holding.Id)).ValuePending.Should().BeFalse();
    }

    [Fact]
    public async Task Repair_FiledSourceRow_IsNeverReset()
    {
        // A filed value is the filer's own claim, not our derivation error — resetting it would
        // loop it through the fallback forever.
        var holding = await SeedHolding(
            value: 5_000_000_000_000L,
            filedValue: 5_000_000_000_000L,
            shares: 100,
            valueSource: ValueSource.Filed
        );

        var repaired = await CreateService().Repair(CancellationToken.None);

        repaired.Should().Be(0);
        (await Reload(holding.Id)).ValuePending.Should().BeFalse();
    }

    // ── Rollups ────────────────────────────────────────────────────────

    [Fact]
    public async Task Repair_HealedRow_RealignsFilingTotalAndStampsAumSnapshotDirty()
    {
        var accession = "0000919079-26-000006";
        var holding = await SeedHolding(
            value: 0L,
            filedValue: 9_571_400_000L,
            shares: 37_713_677,
            accession: accession
        );

        var seedContext = CreateSharedContext();
        seedContext
            .Set<InstitutionalFiling>()
            .Add(
                new InstitutionalFiling
                {
                    Id = Guid.NewGuid(),
                    AccessionNumber = accession,
                    InstitutionalHolderId = holding.InstitutionalHolderId,
                    FilingDate = holding.FilingDate,
                    ReportDate = holding.ReportDate,
                    PositionCount = 1,
                    TotalValue = 0L,
                }
            );
        await seedContext.SaveChangesAsync();

        await CreateService().Repair(CancellationToken.None);

        var verifyContext = CreateSharedContext();
        var filing = await verifyContext
            .Set<InstitutionalFiling>()
            .FirstAsync(f => f.AccessionNumber == accession);
        filing
            .TotalValue.Should()
            .Be(9_571_400_000L, "a healed position with a stale rollup just moves the lie up");

        var snapshot = await verifyContext
            .Set<AumQuarterlySnapshot>()
            .FirstAsync(s => s.ReportDate == holding.ReportDate);
        snapshot.DirtyAt.Should().NotBeNull("the drain worker must rebuild the quarter");
    }

    // ── Idempotency ────────────────────────────────────────────────────

    [Fact]
    public async Task Repair_SecondRunOverHealedDatabase_IsANoOp()
    {
        await SeedHolding(value: 0L, filedValue: 750_000L, shares: 500);
        await SeedHolding(value: 2_000_000_000_000L, filedValue: 1_000_000L, shares: 100);

        var service = CreateService();
        var firstRun = await service.Repair(CancellationToken.None);
        var secondRun = await service.Repair(CancellationToken.None);

        firstRun.Should().Be(2);
        secondRun.Should().Be(0);
    }
}
