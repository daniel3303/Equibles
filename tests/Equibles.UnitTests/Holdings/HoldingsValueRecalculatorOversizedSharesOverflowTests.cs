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

/// <summary>
/// HoldingsValueRecalculator is the batch second-chance resolver that heals every
/// ValuePending holding once a Yahoo close price lands, recomputing Value = Shares ×
/// price. Shares is stored straight from the filer's SSHPRNAMT (ParseLong accepts any
/// value up to long.MaxValue), so a corrupt/oversized count can sit pending until a price
/// arrives. The recompute uses an unguarded (long)(Shares × closePrice) cast with no
/// try/catch: when the decimal product exceeds Int64 it throws OverflowException straight
/// out of Recalculate, aborting the whole nightly pass — every OTHER pending holding then
/// stays unresolved. The contract is graceful per-row degradation (mirror the now-fixed
/// ParseHoldingRow and the sibling Filing13DGXmlParser range-check), not a batch crash.
/// Oracle derived from the contract, not the body.
/// </summary>
public class HoldingsValueRecalculatorOversizedSharesOverflowTests
{
    [Fact(Skip = "GH-3855 — Recalculate throws OverflowException on an oversized pending holding")]
    public async Task Recalculate_PendingHoldingSharesTimesPriceExceedsInt64_DoesNotThrowOverflow()
    {
        await using var db = CreateDbContext();
        var stockId = Guid.NewGuid();
        var reportDate = new DateOnly(2024, 9, 30);
        // long.MaxValue shares × a $2 close overflows Int64 (≈1.84e19 > 9.22e18).
        SeedPendingHolding(db, shares: long.MaxValue, stockId: stockId, reportDate: reportDate);

        var priceProvider = Substitute.For<IStockPriceProvider>();
        priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<(Guid CommonStockId, DateOnly Date), decimal>
                {
                    [(stockId, reportDate)] = 2m,
                }
            );
        var recalculator = BuildRecalculator(db, priceProvider);

        var act = () => recalculator.Recalculate(CancellationToken.None);

        await act.Should()
            .NotThrowAsync<OverflowException>(
                "one oversized pending holding must degrade gracefully, not abort the whole nightly recalc pass and leave every other pending holding unresolved"
            );
    }

    private static EquiblesFinancialDbContext CreateDbContext()
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

    private static void SeedPendingHolding(
        EquiblesFinancialDbContext db,
        long shares,
        Guid stockId,
        DateOnly reportDate
    )
    {
        db.Set<InstitutionalHolding>()
            .Add(
                new InstitutionalHolding
                {
                    InstitutionalHolderId = Guid.NewGuid(),
                    CommonStockId = stockId,
                    FilingDate = new DateOnly(2024, 11, 14),
                    ReportDate = reportDate,
                    Value = 0,
                    Shares = shares,
                    ShareType = ShareType.Shares,
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                    Cusip = "037833100",
                    AccessionNumber = $"0000000000-24-{Guid.NewGuid().ToString("N")[..6]}",
                    ValuePending = true,
                    CreationTime = DateTime.UtcNow,
                    ManagerEntries = [],
                }
            );
        db.SaveChanges();
    }

    private static HoldingsValueRecalculator BuildRecalculator(
        EquiblesFinancialDbContext db,
        IStockPriceProvider priceProvider
    )
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton(priceProvider);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new HoldingsValueRecalculator(
            scopeFactory,
            priceProvider,
            Substitute.For<ILogger<HoldingsValueRecalculator>>()
        );
    }
}
