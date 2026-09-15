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
/// Pins the issuer-first impossible-position scan: a position larger than a trustworthy issuer is
/// withdrawn, an issuer whose own size is nonsense is never judged, and the one-million-share floor
/// that keeps every batch inside the partial index is a floor, not an exclusion.
/// </summary>
public class ImpossiblePositionRepairServiceTests : IDisposable
{
    private static readonly IModuleConfiguration[] Modules =
    [
        new CommonStocksModuleConfiguration(),
        new HoldingsModuleConfiguration(),
        new CorporateActionsModuleConfiguration(),
    ];

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public void Dispose()
    {
        foreach (var ctx in _contexts)
        {
            ctx.Dispose();
        }
    }

    [Fact]
    public async Task Repair_WithdrawsAPositionLargerThanTheIssuer()
    {
        // NaaS: 32.1B ordinary shares filed against a 200M-share ADS issuer.
        var holding = await SeedHolding(
            sharesOutstanding: 200_000_000,
            marketCapitalization: 5_000_000_000,
            shares: 32_098_694_296,
            value: 100_800_000_000
        );

        var service = CreateService();
        (await service.Repair(CancellationToken.None)).Should().Be(1);

        var actual = await Reload(holding.Id);
        actual.Value.Should().Be(0L);
        actual.ValuePending.Should().BeFalse();
        actual.ValueUnavailable.Should().BeTrue();
        actual.Shares.Should().Be(32_098_694_296, "the filer's own count is kept");
        (await service.Repair(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task Repair_LeavesASubFloorPositionOnAMicroFloatAlone()
    {
        // 700k shares is above twice this issuer's 300k float but below the scan's floor, so the
        // batch query never reads it: the documented miss on the smallest floats.
        var holding = await SeedHolding(
            sharesOutstanding: 300_000,
            marketCapitalization: 3_000_000,
            shares: 700_000,
            value: 7_000_000
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(0);

        var actual = await Reload(holding.Id);
        actual.Value.Should().Be(7_000_000L);
        actual.ValueUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task Repair_JudgesAMicroFloatPositionAboveTheFloor()
    {
        var holding = await SeedHolding(
            sharesOutstanding: 300_000,
            marketCapitalization: 3_000_000,
            shares: 1_500_000,
            value: 15_000_000
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(1);

        var actual = await Reload(holding.Id);
        actual.Value.Should().Be(0L);
        actual.ValueUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task Repair_NeverJudgesAnIssuerWhoseSizeIsNonsense()
    {
        // Air Lease: 200 recorded shares beside a correct $7.28B market cap.
        var holding = await SeedHolding(
            sharesOutstanding: 200,
            marketCapitalization: 7_280_000_000,
            shares: 5_000_000,
            value: 250_000_000
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(0);

        (await Reload(holding.Id)).ValueUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task Repair_LeavesAPositionInsideTheIssuerAlone()
    {
        var holding = await SeedHolding(
            sharesOutstanding: 200_000_000,
            marketCapitalization: 5_000_000_000,
            shares: 150_000_000,
            value: 3_750_000_000
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(0);

        (await Reload(holding.Id)).Value.Should().Be(3_750_000_000L);
    }

    private ImpossiblePositionRepairService CreateService() =>
        new(CreateScopeFactory(), Substitute.For<ILogger<ImpossiblePositionRepairService>>());

    private async Task<InstitutionalHolding> SeedHolding(
        long sharesOutstanding,
        double marketCapitalization,
        long shares,
        long value
    )
    {
        var seedContext = CreateSharedContext();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: Guid.NewGuid().ToString()[..4],
            Name: "Issuer",
            MarketCapitalization: marketCapitalization,
            SharesOutStanding: sharesOutstanding
        );
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = Guid.NewGuid().ToString()[..10],
            Name = "Filer",
        };
        var holding = new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = new DateOnly(2026, 3, 31),
            FilingDate = new DateOnly(2026, 5, 10),
            Shares = shares,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = Guid.NewGuid().ToString()[..20],
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

        seedContext.Set<EquityIssuer>().Add(stock);
        seedContext.Set<InstitutionalHolder>().Add(holder);
        seedContext.Set<InstitutionalHolding>().Add(holding);
        await seedContext.SaveChangesAsync();
        return holding;
    }

    private async Task<InstitutionalHolding> Reload(Guid holdingId) =>
        await CreateSharedContext().Set<InstitutionalHolding>().FirstAsync(h => h.Id == holdingId);

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
}
