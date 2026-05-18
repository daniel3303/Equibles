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
/// <see cref="CommonStockManager.SetFiscalYearEnd"/> mirrors the SetCusip
/// no-op-on-unchanged contract: a same-value call must persist nothing (the
/// SEC scraper calls this for every company on every run, so a naive
/// always-save would write the entire stock universe each pass). Invalid
/// SEC-sourced values must be rejected, not stored.
/// </summary>
public class CommonStockManagerSetFiscalYearEndTests
{
    private static EquiblesDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesDbContext(options, [new CommonStocksModuleConfiguration()]);
    }

    private static CommonStockManager NewManager(CommonStockRepository repository) =>
        new(repository, Substitute.For<IPublishEndpoint>());

    [Fact]
    public async Task SetFiscalYearEnd_PreviouslyUndetected_PersistsMonthAndDay()
    {
        var db = NewDb();
        var repository = Substitute.For<CommonStockRepository>(db);
        var stock = new CommonStock { FiscalYearEndMonth = null, FiscalYearEndDay = null };

        await NewManager(repository).SetFiscalYearEnd(stock, 9, 28);

        stock.FiscalYearEndMonth.Should().Be(9);
        stock.FiscalYearEndDay.Should().Be(28);
        await repository.Received(1).SaveChanges();
    }

    [Fact]
    public async Task SetFiscalYearEnd_UnchangedValue_PersistsNothing()
    {
        var db = NewDb();
        var repository = Substitute.For<CommonStockRepository>(db);
        var stock = new CommonStock { FiscalYearEndMonth = 6, FiscalYearEndDay = 30 };

        await NewManager(repository).SetFiscalYearEnd(stock, 6, 30);

        await repository.DidNotReceive().SaveChanges();
    }

    [Fact]
    public async Task SetFiscalYearEnd_NullDayMatchesStoredNullDay_PersistsNothing()
    {
        var db = NewDb();
        var repository = Substitute.For<CommonStockRepository>(db);
        var stock = new CommonStock { FiscalYearEndMonth = 12, FiscalYearEndDay = null };

        await NewManager(repository).SetFiscalYearEnd(stock, 12, null);

        await repository.DidNotReceive().SaveChanges();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(9, 0)]
    [InlineData(9, 32)]
    public async Task SetFiscalYearEnd_InvalidValue_ThrowsAndPersistsNothing(int month, int day)
    {
        var db = NewDb();
        var repository = Substitute.For<CommonStockRepository>(db);
        var stock = new CommonStock();

        var act = () => NewManager(repository).SetFiscalYearEnd(stock, month, day);

        await act.Should().ThrowAsync<DomainValidationException>();
        await repository.DidNotReceive().SaveChanges();
    }

    [Fact]
    public async Task SetFiscalYearEnd_NullStock_Throws()
    {
        var db = NewDb();
        var repository = Substitute.For<CommonStockRepository>(db);

        var act = () => NewManager(repository).SetFiscalYearEnd(null, 9, 28);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
