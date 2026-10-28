using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Against Postgres: the retry pass advances only rows whose ladder step is due, whether their
/// pair has no due row at all or mixes due and not-yet-due rows.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsValueRecalculatorDuePairsTests : IAsyncLifetime
{
    private static readonly DateOnly ReportDate = new(2026, 6, 30);

    private readonly ParadeDbFixture _fixture;
    private readonly IStockPriceProvider _priceProvider = Substitute.For<IStockPriceProvider>();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public HoldingsValueRecalculatorDuePairsTests(ParadeDbFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext CreateSharedContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private HoldingsValueRecalculator CreateRecalculator(
        int settledRowsPerPass = HoldingsValueRecalculator.MaxSettledRowsPerPass
    )
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(CreateSharedContext());
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return new HoldingsValueRecalculator(
            scopeFactory,
            _priceProvider,
            Substitute.For<ILogger<HoldingsValueRecalculator>>()
        )
        {
            SettledRowsPerPass = settledRowsPerPass,
        };
    }

    private static InstitutionalHolding Pending(
        EquityIssuer issuer,
        InstitutionalHolder holder,
        DateTime lastRetryAt
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = ReportDate,
            FilingDate = ReportDate.AddDays(40),
            Shares = 1_000,
            ValuePending = true,
            ValueRetryCount = 1,
            ValueLastRetryAt = lastRetryAt,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
        };

    [Fact]
    public async Task Recalculate_UnpricedPairs_AdvanceOnlyTheirDueRows()
    {
        var now = DateTime.UtcNow;
        var due = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "DUE", Name: "Due");
        var resting = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "RST", Name: "Rest");
        var mixed = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "MIX", Name: "Mixed");
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "1000001",
            Name = "Filer",
        };
        var otherHolder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "1000002",
            Name = "Other filer",
        };
        // Retry 1 waits a week: eight days ago is due, two days ago is not.
        var dueRow = Pending(due, holder, now.AddDays(-8));
        var restingRow = Pending(resting, holder, now.AddDays(-2));
        var mixedDueRow = Pending(mixed, holder, now.AddDays(-8));
        var mixedRestingRow = Pending(mixed, otherHolder, now.AddDays(-2));
        var seed = CreateSharedContext();
        seed.Set<EquityIssuer>().AddRange(due, resting, mixed);
        seed.Set<InstitutionalHolder>().AddRange(holder, otherHolder);
        seed.Set<InstitutionalHolding>().AddRange(dueRow, restingRow, mixedDueRow, mixedRestingRow);
        await seed.SaveChangesAsync();
        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<(Guid, string, DateOnly), decimal>());

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var rows = await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .ToDictionaryAsync(h => h.Id);
        foreach (var advanced in new[] { dueRow, mixedDueRow })
        {
            rows[advanced.Id].ValueRetryCount.Should().Be(2);
            rows[advanced.Id].ValueLastRetryAt.Should().BeAfter(now.AddMinutes(-1));
            rows[advanced.Id].ValuePending.Should().BeTrue();
        }
        foreach (var waiting in new[] { restingRow, mixedRestingRow })
        {
            rows[waiting.Id].ValueRetryCount.Should().Be(1);
            rows[waiting.Id]
                .ValueLastRetryAt.Should()
                .BeCloseTo(waiting.ValueLastRetryAt!.Value, TimeSpan.FromMilliseconds(1));
        }
    }

    // One pass over a pair that mixes every rung: the climbing row gains a step and a stamp, the row
    // on the last rung publishes its filed value across its legs, and the resting row is untouched.
    [Fact]
    public async Task Recalculate_UnpricedPair_ClimbsSettlesAndRestsItsRowsInOnePass()
    {
        var now = DateTime.UtcNow;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "MIX", Name: "Mixed");
        InstitutionalHolder Holder(string cik) =>
            new()
            {
                Id = Guid.NewGuid(),
                Cik = cik,
                Name = "Filer " + cik,
            };
        var climbingHolder = Holder("2000001");
        var exhaustedHolder = Holder("2000002");
        var restingHolder = Holder("2000003");
        var climbing = Pending(issuer, climbingHolder, now.AddDays(-8));
        var exhausted = Pending(issuer, exhaustedHolder, now.AddDays(-31));
        exhausted.ValueRetryCount = 3;
        exhausted.FiledValue = 5_000L;
        exhausted.ManagerEntries =
        [
            new HoldingManagerEntry
            {
                ManagerNumber = 1,
                ManagerName = "Leg A",
                Shares = 250,
                Value = 0L,
            },
            new HoldingManagerEntry
            {
                ManagerNumber = 2,
                ManagerName = "Leg B",
                Shares = 750,
                Value = 0L,
            },
        ];
        var resting = Pending(issuer, restingHolder, now.AddDays(-2));
        var seed = CreateSharedContext();
        seed.Set<EquityIssuer>().Add(issuer);
        seed.Set<InstitutionalHolder>().AddRange(climbingHolder, exhaustedHolder, restingHolder);
        seed.Set<InstitutionalHolding>().AddRange(climbing, exhausted, resting);
        await seed.SaveChangesAsync();
        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<(Guid, string, DateOnly), decimal>());

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var rows = await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .Include(h => h.ManagerEntries)
            .ToDictionaryAsync(h => h.Id);
        rows[climbing.Id].ValueRetryCount.Should().Be(2);
        rows[climbing.Id].ValueLastRetryAt.Should().BeAfter(now.AddMinutes(-1));
        rows[climbing.Id].ValuePending.Should().BeTrue();
        rows[exhausted.Id].ValuePending.Should().BeFalse();
        rows[exhausted.Id].ValueSource.Should().Be(ValueSource.Filed);
        rows[exhausted.Id].Value.Should().Be(5_000L);
        rows[exhausted.Id].ValueRetryCount.Should().Be(4);
        rows[exhausted.Id]
            .ManagerEntries.OrderBy(e => e.ManagerNumber)
            .Select(e => e.Value)
            .Should()
            .Equal(1_250L, 3_750L);
        rows[resting.Id].ValueRetryCount.Should().Be(1);
        rows[resting.Id].ValuePending.Should().BeTrue();
        rows[resting.Id]
            .ValueLastRetryAt.Should()
            .BeCloseTo(resting.ValueLastRetryAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    // The production backlog: rows pending since May that a 13F replay put back on the first rung,
    // stamped two days ago. Pending longer than the whole 68-day ladder, they settle on this pass
    // whatever their count says. Rows inside the ladder's span keep the ladder exactly: a young row
    // waits for its step, a 50-day-old row on rung 2 climbs to the last rung, and one on the last
    // rung retried 10 days ago keeps waiting out its final 30 days.
    [Fact]
    public async Task Recalculate_UnpricedRowsPendingLongerThanTheLadder_SettleDespiteARestartedCount()
    {
        var now = DateTime.UtcNow;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OLD", Name: "Old");
        InstitutionalHolder Holder(string cik) =>
            new()
            {
                Id = Guid.NewGuid(),
                Cik = cik,
                Name = "Filer " + cik,
            };
        var filedHolder = Holder("3000001");
        var unfiledHolder = Holder("3000002");
        var youngHolder = Holder("3000003");
        var climbingHolder = Holder("3000004");
        var lastRungHolder = Holder("3000005");
        var outlivedFiled = Pending(issuer, filedHolder, now.AddDays(-2));
        outlivedFiled.CreationTime = now.AddDays(-150);
        outlivedFiled.FiledValue = 7_500L;
        var outlivedUnfiled = Pending(issuer, unfiledHolder, now.AddDays(-2));
        outlivedUnfiled.CreationTime = now.AddDays(-150);
        var young = Pending(issuer, youngHolder, now.AddDays(-2));
        young.CreationTime = now.AddDays(-10);
        young.FiledValue = 2_000L;
        var climbing = Pending(issuer, climbingHolder, now.AddDays(-31));
        climbing.CreationTime = now.AddDays(-50);
        climbing.ValueRetryCount = 2;
        climbing.FiledValue = 3_000L;
        var lastRung = Pending(issuer, lastRungHolder, now.AddDays(-10));
        lastRung.CreationTime = now.AddDays(-50);
        lastRung.ValueRetryCount = 3;
        lastRung.FiledValue = 4_000L;
        var seed = CreateSharedContext();
        seed.Set<EquityIssuer>().Add(issuer);
        seed.Set<InstitutionalHolder>()
            .AddRange(filedHolder, unfiledHolder, youngHolder, climbingHolder, lastRungHolder);
        seed.Set<InstitutionalHolding>()
            .AddRange(outlivedFiled, outlivedUnfiled, young, climbing, lastRung);
        await seed.SaveChangesAsync();
        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<(Guid, string, DateOnly), decimal>());

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var rows = await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .ToDictionaryAsync(h => h.Id);
        rows[outlivedFiled.Id].ValuePending.Should().BeFalse();
        rows[outlivedFiled.Id].ValueSource.Should().Be(ValueSource.Filed);
        rows[outlivedFiled.Id].Value.Should().Be(7_500L);
        rows[outlivedUnfiled.Id].ValuePending.Should().BeFalse();
        rows[outlivedUnfiled.Id].ValueUnavailable.Should().BeTrue();
        rows[young.Id].ValuePending.Should().BeTrue();
        rows[young.Id].ValueRetryCount.Should().Be(1);
        rows[young.Id]
            .ValueLastRetryAt.Should()
            .BeCloseTo(young.ValueLastRetryAt!.Value, TimeSpan.FromMilliseconds(1));
        rows[climbing.Id].ValuePending.Should().BeTrue();
        rows[climbing.Id].ValueRetryCount.Should().Be(3);
        rows[lastRung.Id].ValuePending.Should().BeTrue();
        rows[lastRung.Id].ValueRetryCount.Should().Be(3);
        rows[lastRung.Id]
            .ValueLastRetryAt.Should()
            .BeCloseTo(lastRung.ValueLastRetryAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    // A pass settles at most its budget, newest quarter first. The quarter it leaves for later is
    // neither stamped nor climbed meanwhile, and settles on the next pass.
    [Fact]
    public async Task Recalculate_SettlingBudgetSpent_LeavesOlderQuartersUntouchedForTheNextPass()
    {
        var now = DateTime.UtcNow;
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "BIG", Name: "Big");
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "4000001",
            Name = "Filer",
        };
        var newer = Pending(issuer, holder, now.AddDays(-2));
        newer.CreationTime = now.AddDays(-150);
        newer.FiledValue = 1_000L;
        var older = Pending(issuer, holder, now.AddDays(-2));
        older.ReportDate = ReportDate.AddMonths(-3);
        older.CreationTime = now.AddDays(-150);
        older.FiledValue = 2_000L;
        var seed = CreateSharedContext();
        seed.Set<EquityIssuer>().Add(issuer);
        seed.Set<InstitutionalHolder>().Add(holder);
        seed.Set<InstitutionalHolding>().AddRange(newer, older);
        await seed.SaveChangesAsync();
        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<(Guid, string, DateOnly), decimal>());

        await CreateRecalculator(settledRowsPerPass: 1).Recalculate(CancellationToken.None);

        var afterFirst = await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .ToDictionaryAsync(h => h.Id);
        afterFirst[newer.Id].ValuePending.Should().BeFalse();
        afterFirst[newer.Id].Value.Should().Be(1_000L);
        afterFirst[older.Id].ValuePending.Should().BeTrue();
        afterFirst[older.Id].ValueRetryCount.Should().Be(1);
        afterFirst[older.Id]
            .ValueLastRetryAt.Should()
            .BeCloseTo(older.ValueLastRetryAt!.Value, TimeSpan.FromMilliseconds(1));

        await CreateRecalculator(settledRowsPerPass: 1).Recalculate(CancellationToken.None);

        var afterSecond = await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .FirstAsync(h => h.Id == older.Id);
        afterSecond.ValuePending.Should().BeFalse();
        afterSecond.ValueSource.Should().Be(ValueSource.Filed);
        afterSecond.Value.Should().Be(2_000L);
    }
}
