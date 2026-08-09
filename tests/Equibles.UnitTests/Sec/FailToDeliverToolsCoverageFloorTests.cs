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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Equibles.UnitTests.Sec;

// Contract (#7045): "an absent date means no reported fails" is only true INSIDE the
// fully covered window. The table holds a sparse pre-full-universe trickle era and a
// full-universe era — the floor must be the first DENSE settlement date (per-date row
// count at or above the repository threshold), because a raw earliest date would let
// years of partial ingestion masquerade as covered.
public class FailToDeliverToolsCoverageFloorTests
{
    [Fact]
    public async Task GetFailsToDeliver_ScopesTheAbsenceClaimToTheDenseCoverageFloor()
    {
        var options = NewDbOptions();
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
            seed.Add(
                new FailToDeliver
                {
                    CommonStockId = stock.Id,
                    SettlementDate = new DateOnly(2026, 3, 2),
                    Quantity = 1000,
                    Price = 10m,
                }
            );
            // Another stock carries a sparse trickle-era row BELOW the per-date
            // threshold — it must NOT set the floor — plus enough same-date rows to
            // make 2026-03-02 the first dense date.
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
            for (var i = 0; i < FailToDeliverRepository.MinRowsPerCoveredDate; i++)
            {
                seed.Add(
                    new FailToDeliver
                    {
                        CommonStockId = other.Id,
                        SettlementDate = new DateOnly(2026, 3, 2),
                        Quantity = 10 + i,
                        Price = 1m,
                    }
                );
            }
            seed.SaveChanges();
        }

        using var ctx = NewContext(options);
        var tools = NewTools(ctx);

        var result = await tools.GetFailsToDeliver(
            "FTDC",
            startDate: "2026-01-01",
            endDate: "2026-04-01"
        );

        result.Should().Contain("Full-universe coverage begins 2026-03-02");
        result
            .Should()
            .Contain(
                "within the covered window, dates absent from the table had no reported fails",
                "the absence promise must be scoped, never absolute"
            );
        result
            .Should()
            .Contain(
                "earlier dates are only partially covered",
                "the sparse trickle era must not read as covered"
            );
        result
            .Should()
            .NotContain("begins 2026-01-15", "a below-threshold date must not set the floor");
    }

    [Fact]
    public async Task DenseCoverageFloor_IgnoresSparseDatesBelowThreshold()
    {
        var options = NewDbOptions();
        using (var seed = NewContext(options))
        {
            var stock = new CommonStock
            {
                Ticker = "FTDS",
                Name = "Sparse Corp",
                Cik = "3",
            };
            seed.Add(stock);
            seed.SaveChanges();
            // One row short of the threshold on the earlier date: not covered.
            for (var i = 0; i < FailToDeliverRepository.MinRowsPerCoveredDate - 1; i++)
            {
                seed.Add(
                    new FailToDeliver
                    {
                        CommonStockId = stock.Id,
                        SettlementDate = new DateOnly(2025, 6, 2),
                        Quantity = i + 1,
                        Price = 1m,
                    }
                );
            }
            // Exactly the threshold on the later date: the floor.
            for (var i = 0; i < FailToDeliverRepository.MinRowsPerCoveredDate; i++)
            {
                seed.Add(
                    new FailToDeliver
                    {
                        CommonStockId = stock.Id,
                        SettlementDate = new DateOnly(2026, 3, 2),
                        Quantity = i + 1,
                        Price = 1m,
                    }
                );
            }
            seed.SaveChanges();
        }

        using var ctx = NewContext(options);
        var floor = await new FailToDeliverRepository(ctx)
            .GetDenseCoverageFloor()
            .Select(d => (DateOnly?)d)
            .FirstOrDefaultAsync();

        floor.Should().Be(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public async Task DenseCoverageFloor_EmptyTable_YieldsNull()
    {
        using var ctx = NewContext(NewDbOptions());
        var floor = await new FailToDeliverRepository(ctx)
            .GetDenseCoverageFloor()
            .Select(d => (DateOnly?)d)
            .FirstOrDefaultAsync();

        floor.Should().BeNull();
    }

    private static FailToDeliverTools NewTools(EquiblesFinancialDbContext ctx) =>
        new(
            new FailToDeliverRepository(ctx),
            new CommonStockRepository(ctx),
            new MemoryCache(new MemoryCacheOptions()),
            new ErrorManager(null),
            NullLogger<FailToDeliverTools>.Instance
        );

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
