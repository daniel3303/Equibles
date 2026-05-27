using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Exceptions;
using Equibles.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// SetFiscalYearEnd validates month 1-12 and day 1-31 independently, so
/// month=2, day=31 passes even though February 31 never exists. Downstream
/// code constructing a DateOnly from this would throw.
/// </summary>
public class CommonStockManagerSetFiscalYearEndInvalidDayForMonthTests
{
    [Fact]
    public async Task SetFiscalYearEnd_February31_ThrowsDomainValidation()
    {
        var db = new EquiblesFinancialDbContext(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        var repository = Substitute.For<CommonStockRepository>(db);
        var sut = new CommonStockManager(repository, Substitute.For<IPublishEndpoint>());
        var stock = new CommonStock();

        var act = () => sut.SetFiscalYearEnd(stock, 2, 31);

        await act.Should().ThrowAsync<DomainValidationException>();
        await repository.DidNotReceive().SaveChanges();
    }
}
