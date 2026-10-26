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

    private HoldingsValueRecalculator CreateRecalculator()
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
        );
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
}
