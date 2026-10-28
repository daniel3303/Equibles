using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data.Models;
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
/// Southern Copper's 1.0106:1 stock dividend, captured weeks before its effective date, held every
/// pending Q1 2026 SCCO position as an unreconciled split basis. A split that has not taken effect
/// has moved no close and no count, so it must not block the valuation; one that has taken effect
/// still does until its price adjustment is stamped.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsValueRecalculatorAnnouncedSplitTests : IAsyncLifetime
{
    private static readonly DateOnly ReportDate = new(2026, 3, 31);
    private const decimal SccoClose = 168.3364m;

    private readonly ParadeDbFixture _fixture;
    private readonly IStockPriceProvider _priceProvider = Substitute.For<IStockPriceProvider>();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public HoldingsValueRecalculatorAnnouncedSplitTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

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

    private async Task<InstitutionalHolding> SeedPendingPositionWithSplit(DateOnly effectiveDate)
    {
        var seed = CreateSharedContext();
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "SCCO",
            Name: "Southern Copper Corp"
        );
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "1000001",
            Name = "Filer",
        };
        var holding = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = ReportDate,
            FilingDate = new DateOnly(2026, 5, 14),
            Shares = 10_000,
            FiledValue = 1_683_364L,
            ValuePending = true,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = "0001000001-26-000001",
        };
        seed.Set<EquityIssuer>().Add(issuer);
        seed.Set<InstitutionalHolder>().Add(holder);
        seed.Set<InstitutionalHolding>().Add(holding);
        await seed.SaveChangesAsync();
        seed.Add(
            new StockSplit
            {
                EquityIssuerId = issuer.Id,
                EquityListingId = issuer.Presentation.EquityListingId,
                PriceSeriesTicker = "SCCO",
                EffectiveDate = effectiveDate,
                Numerator = 1.0106m,
                Denominator = 1m,
                Source = StockSplitSource.External,
                PriceAdjustmentAppliedTime = null,
            }
        );
        await seed.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<(Guid, string, DateOnly), decimal>
                {
                    [(issuer.Id, null, ReportDate)] = SccoClose,
                }
            );
        return holding;
    }

    private async Task<InstitutionalHolding> Reload(Guid id) =>
        await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .FirstAsync(h => h.Id == id);

    [Fact]
    public async Task Recalculate_SplitNotYetEffective_DerivesTheValue()
    {
        var holding = await SeedPendingPositionWithSplit(
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(8)
        );

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeFalse();
        updated.ValueSource.Should().Be(ValueSource.Derived);
        updated.Value.Should().Be((long)(10_000 * SccoClose));
    }

    [Fact]
    public async Task Recalculate_EffectiveSplitAwaitingPriceAdjustment_StaysPending()
    {
        var holding = await SeedPendingPositionWithSplit(
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)
        );

        await CreateRecalculator().Recalculate(CancellationToken.None);

        var updated = await Reload(holding.Id);
        updated.ValuePending.Should().BeTrue();
        updated.Value.Should().Be(0L);
    }
}
