using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

// SetCusip's alias contract: replacing a non-null CUSIP records the retired
// value as a CommonStockCusipAlias so import-time resolution keeps mapping it
// to the stock (laggard 13F filers and historical data sets reference the old
// CUSIP long after the change). First-time seeding (null → value) retires
// nothing, so it must record nothing; and a CUSIP already present in the alias
// table must not be added twice (the unique index would abort the save).
public class CommonStockManagerSetCusipRecordsAliasTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
    }

    private static async Task<(
        CommonStockManager Sut,
        EquiblesFinancialDbContext Db,
        CommonStock Stock
    )> Arrange(string initialCusip)
    {
        var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "BBUC",
            Name = "Brookfield Business Corp",
            Cik = "1654795",
            Cusip = initialCusip,
        };
        db.Set<CommonStock>().Add(stock);
        await db.SaveChangesAsync();

        var sut = new CommonStockManager(new CommonStockRepository(db), Substitute.For<IBus>());
        return (sut, db, stock);
    }

    [Fact]
    public async Task SetCusip_ReplacingExistingCusip_RecordsRetiredCusipAsAlias()
    {
        var (sut, db, stock) = await Arrange("11259V106");

        await sut.SetCusip(stock, "113006100");

        stock.Cusip.Should().Be("113006100");
        var alias = await db.Set<CommonStockCusipAlias>().SingleAsync();
        alias.Cusip.Should().Be("11259V106");
        alias.CommonStockId.Should().Be(stock.Id);
    }

    [Fact]
    public async Task SetCusip_FirstTimeSeedingFromNull_RecordsNoAlias()
    {
        var (sut, db, stock) = await Arrange(null);

        await sut.SetCusip(stock, "113006100");

        stock.Cusip.Should().Be("113006100");
        (await db.Set<CommonStockCusipAlias>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task SetCusip_RetiredCusipAlreadyAliased_DoesNotDuplicateAliasRow()
    {
        var (sut, db, stock) = await Arrange("11259V106");
        db.Set<CommonStockCusipAlias>()
            .Add(new CommonStockCusipAlias { CommonStockId = stock.Id, Cusip = "11259V106" });
        await db.SaveChangesAsync();

        await sut.SetCusip(stock, "113006100");

        stock.Cusip.Should().Be("113006100");
        var aliases = await db.Set<CommonStockCusipAlias>().ToListAsync();
        aliases.Should().ContainSingle().Which.Cusip.Should().Be("11259V106");
    }
}
