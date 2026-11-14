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

    private static EquityIdentityManager NewManager(EquityIssuerRepository repository) =>
        new(repository, Substitute.For<IBus>());

    [Fact]
    public async Task SetFiscalYearEnd_PreviouslyUndetected_PersistsMonthAndDay()
    {
        var db = NewDb();
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            FiscalYearEndMonth: null,
            FiscalYearEndDay: null
        );

        await NewManager(repository).SetFiscalYearEnd(stock, 9, 28);

        stock.FiscalYearEndMonth.Should().Be(9);
        stock.FiscalYearEndDay.Should().Be(28);
        await repository.Received(1).SaveChanges();
    }

    [Fact]
    public async Task SetFiscalYearEnd_FirstWeekOfAMonth_IsStoredAsThePreviousMonthsLastDay()
    {
        // The Saturday nearest January 31 falls on February 1 some years.
        var db = NewDb();
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            FiscalYearEndMonth: null,
            FiscalYearEndDay: null
        );

        await NewManager(repository).SetFiscalYearEnd(stock, 2, 1);

        stock.FiscalYearEndMonth.Should().Be(1);
        stock.FiscalYearEndDay.Should().Be(31);
        stock.FiscalYearEndMonthChangedAt.Should().BeNull();
    }

    [Fact]
    public async Task SetFiscalYearEnd_EarlyJanuary_StaysAsStated()
    {
        var db = NewDb();
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            FiscalYearEndMonth: null,
            FiscalYearEndDay: null
        );

        await NewManager(repository).SetFiscalYearEnd(stock, 1, 3);

        stock.FiscalYearEndMonth.Should().Be(1);
        stock.FiscalYearEndDay.Should().Be(3);
    }

    [Theory]
    [InlineData(1, 19, 12, 31, true)]
    [InlineData(9, 27, 9, 26, false)]
    // An early-January year end reads as December, whichever of the two is recorded.
    [InlineData(1, 3, 12, 31, false)]
    [InlineData(12, 31, 1, 3, false)]
    [InlineData(2, 1, 1, 31, true)]
    public async Task SetFiscalYearEnd_RecordedMonthReplaced_IsStampedOnlyWhenTheMonthChanges(
        int recordedMonth,
        int recordedDay,
        int month,
        int day,
        bool stamped
    )
    {
        var db = NewDb();
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            FiscalYearEndMonth: recordedMonth,
            FiscalYearEndDay: recordedDay
        );

        await NewManager(repository).SetFiscalYearEnd(stock, month, day);

        stock.FiscalYearEndMonth.Should().Be(month);
        (stock.FiscalYearEndMonthChangedAt != null).Should().Be(stamped);
        stock.FiscalYearEndMonthBeforeChange.Should().Be(stamped ? recordedMonth : null);
    }

    [Fact]
    public async Task SetFiscalYearEnd_UnchangedValue_PersistsNothing()
    {
        var db = NewDb();
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            FiscalYearEndMonth: 6,
            FiscalYearEndDay: 30
        );

        await NewManager(repository).SetFiscalYearEnd(stock, 6, 30);

        await repository.DidNotReceive().SaveChanges();
    }

    [Fact]
    public async Task SetFiscalYearEnd_NullDayMatchesStoredNullDay_PersistsNothing()
    {
        var db = NewDb();
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            FiscalYearEndMonth: 12,
            FiscalYearEndDay: null
        );

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
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create();

        var act = () => NewManager(repository).SetFiscalYearEnd(stock, month, day);

        await act.Should().ThrowAsync<DomainValidationException>();
        await repository.DidNotReceive().SaveChanges();
    }

    [Fact]
    public async Task SetFiscalYearEnd_NullStock_Throws()
    {
        var db = NewDb();
        EquityIssuerRepository repository = Substitute.For<EquityIssuerRepository>(db);

        var act = () => NewManager(repository).SetFiscalYearEnd(null, 9, 28);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
