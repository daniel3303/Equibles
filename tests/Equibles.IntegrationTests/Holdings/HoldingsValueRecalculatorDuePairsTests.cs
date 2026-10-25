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
}
