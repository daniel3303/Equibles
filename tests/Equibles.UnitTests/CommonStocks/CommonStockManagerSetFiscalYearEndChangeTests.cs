using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Adversarial sibling to <see cref="CommonStockManagerSetFiscalYearEndTests"/>,
/// which only pins null→value and same→same. The docstring says "a no-op
/// change persists nothing" — the contrapositive (a *real* change to an
/// already-detected value must update the fields and persist) is the
/// unverified branch of the idempotency guard. Companies do change their
/// fiscal year-end; a stale guard here would silently keep the old quarter.
/// </summary>
public class CommonStockManagerSetFiscalYearEndChangeTests
{
    [Fact]
    public async Task SetFiscalYearEnd_ChangedFromPreviousValue_UpdatesAndPersists()
    {
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new EquiblesDbContext(options, [new CommonStocksModuleConfiguration()]);
        var repository = Substitute.For<CommonStockRepository>(db);
        var manager = new CommonStockManager(repository, Substitute.For<IPublishEndpoint>());
        var stock = new CommonStock { FiscalYearEndMonth = 6, FiscalYearEndDay = 30 };

        await manager.SetFiscalYearEnd(stock, 9, 28);

        stock.FiscalYearEndMonth.Should().Be(9);
        stock.FiscalYearEndDay.Should().Be(28);
        await repository.Received(1).SaveChanges();
    }
}
