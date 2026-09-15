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
/// withdrawn, an issuer whose own size is nonsense is never judged, an issuer below the
/// one-million-share index floor is still judged at its own bar, and an issuer that three distinct
/// filers exceed is refused instead of every filer being accused, as is an issuer whose other
/// filers together already hold more than its stored size.
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
    public async Task Repair_JudgesAMicroFloatAtItsOwnBarBelowTheFloor()
    {
        // 700k shares is above twice this issuer's 300k float but below the index floor: the
        // issuer is asked apart, at its true bar, so the position is still withdrawn.
        var holding = await SeedHolding(
            sharesOutstanding: 300_000,
            marketCapitalization: 3_000_000,
            shares: 700_000,
            value: 7_000_000
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(1);

        var actual = await Reload(holding.Id);
        actual.Value.Should().Be(0L);
        actual.ValueUnavailable.Should().BeTrue();
    }

    [Fact]
    public async Task Repair_LeavesAMicroFloatPositionInsideItsBarAlone()
    {
        var holding = await SeedHolding(
            sharesOutstanding: 300_000,
            marketCapitalization: 3_000_000,
            shares: 500_000,
            value: 5_000_000
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(0);

        var actual = await Reload(holding.Id);
        actual.Value.Should().Be(5_000_000L);
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

    [Fact]
    public async Task Repair_LeavesAnIssuerAloneWhenThreeFilersExceedIt()
    {
        // NFE: size stored in thousands (114,254 shares beside a $1.88M market cap) passes the
        // ratio guard, so every real holder reads as impossible. Three independent filers above
        // it corroborate each other, and the issuer is refused rather than every holder accused.
        var holdings = await SeedHoldings(
            sharesOutstanding: 114_254,
            marketCapitalization: 1_882_904,
            positions:
            [
                (25_559_846, 100_000_000),
                (12_000_000, 48_000_000),
                (3_000_000, 12_000_000),
            ]
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(0);

        foreach (var holding in holdings)
        {
            var actual = await Reload(holding.Id);
            actual.ValueUnavailable.Should().BeFalse();
            actual.Value.Should().Be(holding.Value);
        }
    }

    [Fact]
    public async Task Repair_WithdrawsWhenOnlyTwoFilersExceedTheIssuer()
    {
        var holdings = await SeedHoldings(
            sharesOutstanding: 114_254,
            marketCapitalization: 1_882_904,
            positions: [(25_559_846, 100_000_000), (12_000_000, 48_000_000)]
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(2);

        foreach (var holding in holdings)
        {
            (await Reload(holding.Id)).ValueUnavailable.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Repair_CountsFilersNotRows_WhenOneFilerExceedsAcrossQuarters()
    {
        // One filer over three quarters is one witness, not three: the position is withdrawn.
        var holdings = await SeedHoldings(
            sharesOutstanding: 200_000_000,
            marketCapitalization: 5_000_000_000,
            positions: [(32_098_694_296, 100_800_000_000)],
            quarters: 3
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(3);

        foreach (var holding in holdings)
        {
            (await Reload(holding.Id)).ValueUnavailable.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Repair_LeavesAnIssuerAloneWhenTheOtherFilersAlreadyOutholdIt()
    {
        // PRPL: 4.36M stored shares, one filer at 20M reads as impossible, but five other filers
        // inside the bar together hold 40M on the same quarter, so the size is what is wrong.
        var holdings = await SeedHoldings(
            sharesOutstanding: 4_359_632,
            marketCapitalization: 40_000_000,
            positions:
            [
                (20_000_000, 180_000_000),
                (8_000_000, 72_000_000),
                (8_000_000, 72_000_000),
                (8_000_000, 72_000_000),
                (8_000_000, 72_000_000),
                (8_000_000, 72_000_000),
            ]
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(0);

        foreach (var holding in holdings)
        {
            (await Reload(holding.Id)).ValueUnavailable.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Repair_StillWithdrawsWhenTheOtherFilersFitInsideTheIssuer()
    {
        // NaaS beside two ordinary holders: the rest of the market fits in 200M shares, so the
        // lone 32.1B-share filer is the one that is wrong.
        var holdings = await SeedHoldings(
            sharesOutstanding: 200_000_000,
            marketCapitalization: 5_000_000_000,
            positions:
            [
                (32_098_694_296, 100_800_000_000),
                (30_000_000, 750_000_000),
                (20_000_000, 500_000_000),
            ]
        );

        (await CreateService().Repair(CancellationToken.None)).Should().Be(1);

        (await Reload(holdings[0].Id)).ValueUnavailable.Should().BeTrue();
        (await Reload(holdings[1].Id)).ValueUnavailable.Should().BeFalse();
        (await Reload(holdings[2].Id)).ValueUnavailable.Should().BeFalse();
    }

    private ImpossiblePositionRepairService CreateService() =>
        new(CreateScopeFactory(), Substitute.For<ILogger<ImpossiblePositionRepairService>>());

    private async Task<InstitutionalHolding> SeedHolding(
        long sharesOutstanding,
        double marketCapitalization,
        long shares,
        long value
    ) => (await SeedHoldings(sharesOutstanding, marketCapitalization, [(shares, value)]))[0];

    // One issuer, one distinct filer per position, each filer reporting the same position on
    // every quarter asked for.
    private async Task<List<InstitutionalHolding>> SeedHoldings(
        long sharesOutstanding,
        double marketCapitalization,
        (long Shares, long Value)[] positions,
        int quarters = 1
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
        seedContext.Set<EquityIssuer>().Add(stock);

        var holdings = new List<InstitutionalHolding>();
        foreach (var (shares, value) in positions)
        {
            var holder = new InstitutionalHolder
            {
                Id = Guid.NewGuid(),
                Cik = Guid.NewGuid().ToString()[..10],
                Name = "Filer",
            };
            seedContext.Set<InstitutionalHolder>().Add(holder);
            for (var quarter = 0; quarter < quarters; quarter++)
            {
                var holding = new InstitutionalHolding
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    InstitutionalHolderId = holder.Id,
                    ReportDate = new DateOnly(2026, 3, 31).AddMonths(-3 * quarter),
                    FilingDate = new DateOnly(2026, 5, 10).AddMonths(-3 * quarter),
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
                seedContext.Set<InstitutionalHolding>().Add(holding);
                holdings.Add(holding);
            }
        }

        await seedContext.SaveChangesAsync();
        return holdings;
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
