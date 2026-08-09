using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Sec;

// Contract (#7045): "an absent date means no reported fails" is only true INSIDE the
// covered window — the ingest lane is forward-only from its first run, so the earliest
// stored settlement date is a coverage floor, and the tool must scope the absence claim
// with it instead of converting the floor into a factual claim about earlier dates.
public class FailToDeliverToolsCoverageFloorTests
{
    [Fact]
    public async Task GetFailsToDeliver_ScopesTheAbsenceClaimToTheCoverageFloor()
    {
        var options = NewDbOptions();
        Guid stockId;
        using (var seed = NewContext(options))
        {
            var stock = new CommonStock
            {
                Ticker = "FTDC",
                Name = "Coverage Corp",
                Cik = "1",
            };
            seed.Add(stock);
            seed.SaveChanges();
            stockId = stock.Id;
            seed.Add(
                new FailToDeliver
                {
                    CommonStockId = stockId,
                    SettlementDate = new DateOnly(2026, 3, 2),
                    Quantity = 1000,
                    Price = 10m,
                }
            );
            // Another stock's older row sets the GLOBAL floor — the claim is about the
            // whole table's coverage, not one ticker's rows.
            var other = new CommonStock
            {
                Ticker = "FTDO",
                Name = "Other Corp",
                Cik = "2",
            };
            seed.Add(other);
            seed.SaveChanges();
            seed.Add(
                new FailToDeliver
                {
                    CommonStockId = other.Id,
                    SettlementDate = new DateOnly(2026, 1, 15),
                    Quantity = 5,
                    Price = 1m,
                }
            );
            seed.SaveChanges();
        }

        using var ctx = NewContext(options);
        var tools = new FailToDeliverTools(
            new FailToDeliverRepository(ctx),
            new CommonStockRepository(ctx),
            new ErrorManager(null),
            NullLogger<FailToDeliverTools>.Instance
        );

        var result = await tools.GetFailsToDeliver(
            "FTDC",
            startDate: "2026-01-01",
            endDate: "2026-04-01"
        );

        result.Should().Contain("Coverage starts 2026-01-15");
        result
            .Should()
            .Contain(
                "within the covered window, dates absent from the table had no reported fails",
                "the absence promise must be scoped, never absolute"
            );
        result.Should().Contain("earlier dates are not covered");
    }

    private static DbContextOptions<EquiblesFinancialDbContext> NewDbOptions() =>
        new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString(), new InMemoryDatabaseRoot())
            .EnableServiceProviderCaching(false)
            .Options;

    private static EquiblesFinancialDbContext NewContext(
        DbContextOptions<EquiblesFinancialDbContext> options
    )
    {
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                // The full Sec module registers document entities whose smart-enum
                // constructors the in-memory provider cannot bind; the test only needs
                // the FTD table.
                new FailToDeliverOnlyModule(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private sealed class FailToDeliverOnlyModule : Equibles.Data.IFinancialModule
    {
        public void ConfigureEntities(ModelBuilder builder)
        {
            builder.Entity<FailToDeliver>();
        }
    }
}
