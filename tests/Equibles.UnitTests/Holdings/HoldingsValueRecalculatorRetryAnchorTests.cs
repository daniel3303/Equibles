using Equibles.CommonStocks.Data;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsValueRecalculatorRetryAnchorTests
{
    // The backoff window is anchored on ValueLastRetryAt ?? CreationTime. Once a holding has been
    // retried, the NEXT attempt must gate on the LAST retry, not the original creation. A holding
    // created long ago but retried 3 days back (ValueRetryCount=1 → 7-day delay) is still inside its
    // window and must NOT be bumped. Anchoring on the old CreationTime would re-bump it early — a
    // retry storm. The existing window test uses a null ValueLastRetryAt (anchor = CreationTime), so
    // only this pins the re-retry anchor. Oracle from the contract.
    [Fact]
    public async Task Recalculate_RetriedHoldingInsideLaterBackoffWindow_AnchorsOnLastRetryNotCreation()
    {
        var harness = new Harness();
        await using var db = harness.CreateDbContext();
        SeedHolding(
            db,
            commonStockId: Guid.NewGuid(),
            // Created 100 days ago, but already retried once 3 days ago. RetryCount=1 → 7-day delay.
            creationTime: DateTime.UtcNow.AddDays(-100),
            valueRetryCount: 1,
            valueLastRetryAt: DateTime.UtcNow.AddDays(-3)
        );

        harness
            .PriceProvider.GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new Dictionary<(Guid CommonStockId, DateOnly Date), decimal>());

        await harness.BuildRecalculator(db).Recalculate(CancellationToken.None);

        var only = await db.Set<InstitutionalHolding>().AsNoTracking().SingleAsync();
        only.ValueRetryCount.Should().Be(1);
        only.ValuePending.Should().BeTrue();
    }

    private static void SeedHolding(
        EquiblesFinancialDbContext db,
        Guid commonStockId,
        DateTime creationTime,
        int valueRetryCount,
        DateTime valueLastRetryAt
    )
    {
        db.Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = Guid.NewGuid(),
                    CommonStockId = commonStockId,
                    FilingDate = new DateOnly(2024, 11, 14),
                    ReportDate = new DateOnly(2024, 9, 30),
                    Value = 0,
                    Shares = 500,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    Cusip = "037833100",
                    AccessionNumber = $"0000000000-24-{Guid.NewGuid().ToString("N")[..6]}",
                    ValuePending = true,
                    ValueRetryCount = valueRetryCount,
                    ValueLastRetryAt = valueLastRetryAt,
                    CreationTime = creationTime,
                    ManagerEntries = [],
                }
            );
        db.SaveChanges();
    }

    private sealed class Harness
    {
        public IStockPriceProvider PriceProvider { get; } = Substitute.For<IStockPriceProvider>();
        public IServiceScopeFactory ScopeFactory { get; private set; }

        public EquiblesFinancialDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options;
            var modules = new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
            };
            var db = new EquiblesFinancialDbContext(options, modules);
            db.Database.EnsureCreated();
            return db;
        }

        public HoldingsValueRecalculator BuildRecalculator(EquiblesFinancialDbContext db)
        {
            var services = new ServiceCollection();
            services.AddSingleton(db);
            services.AddSingleton(PriceProvider);
            var provider = services.BuildServiceProvider();
            ScopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

            return new HoldingsValueRecalculator(
                ScopeFactory,
                PriceProvider,
                Substitute.For<ILogger<HoldingsValueRecalculator>>()
            );
        }
    }
}
