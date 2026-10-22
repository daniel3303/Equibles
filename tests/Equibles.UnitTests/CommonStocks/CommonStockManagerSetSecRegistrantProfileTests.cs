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
/// <see cref="EquityIdentityManager.SetSecRegistrantProfile"/> mirrors EDGAR's current header
/// for the domicile codes and file number, but a business development company election only
/// moves forward: EDGAR's recent-filings list drops old filings, and forgetting an election
/// would make a BDC look like an operating company.
/// </summary>
public class CommonStockManagerSetSecRegistrantProfileTests
{
    private static EquityIssuerRepository NewRepository()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );
        return Substitute.For<EquityIssuerRepository>(db);
    }

    private static EquityIdentityManager NewManager(EquityIssuerRepository repository) =>
        new(repository, Substitute.For<IBus>());

    [Fact]
    public async Task SetSecRegistrantProfile_NewHeader_PersistsTrimmedCodesAndNullsBlanks()
    {
        var repository = NewRepository();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create();

        await NewManager(repository)
            .SetSecRegistrantProfile(
                stock,
                " ",
                "MI ",
                "001-34960",
                null,
                null,
                new DateOnly(2026, 10, 22),
                new DateOnly(2026, 10, 22)
            );

        stock.StateOfIncorporation.Should().BeNull();
        stock.BusinessStateOrCountry.Should().Be("MI");
        stock.PeriodicReportFileNumber.Should().Be("001-34960");
        stock.BdcHistoryReadOn.Should().Be(new DateOnly(2026, 10, 22));
        stock.SecRegistrantProfileReadOn.Should().Be(new DateOnly(2026, 10, 22));
        await repository.Received(1).SaveChanges();
    }

    [Fact]
    public async Task SetSecRegistrantProfile_ElectionMissingFromRecentList_KeepsStoredElection()
    {
        var repository = NewRepository();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create();
        stock.BdcElectionFiledOn = new DateOnly(2011, 4, 15);
        stock.BdcHistoryReadOn = new DateOnly(2026, 10, 1);

        await NewManager(repository)
            .SetSecRegistrantProfile(stock, "DE", "TX", "001-36364", null, null, null, null);

        stock.BdcElectionFiledOn.Should().Be(new DateOnly(2011, 4, 15));
        stock.BdcHistoryReadOn.Should().Be(new DateOnly(2026, 10, 1));
    }

    [Fact]
    public async Task SetSecRegistrantProfile_LaterWithdrawal_IsRecordedBesideTheElection()
    {
        var repository = NewRepository();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create();
        stock.BdcElectionFiledOn = new DateOnly(2014, 3, 1);

        await NewManager(repository)
            .SetSecRegistrantProfile(
                stock,
                "MD",
                "NY",
                "001-40000",
                new DateOnly(2012, 1, 5),
                new DateOnly(2025, 6, 30),
                null,
                null
            );

        stock.BdcElectionFiledOn.Should().Be(new DateOnly(2014, 3, 1));
        stock.BdcWithdrawalFiledOn.Should().Be(new DateOnly(2025, 6, 30));
    }

    [Fact]
    public async Task SetSecRegistrantProfile_UnchangedProfile_PersistsNothing()
    {
        var repository = NewRepository();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create();
        stock.StateOfIncorporation = "DE";
        stock.BusinessStateOrCountry = "CA";
        stock.PeriodicReportFileNumber = "814-00755";
        stock.BdcHistoryReadOn = new DateOnly(2026, 10, 20);
        stock.SecRegistrantProfileReadOn = new DateOnly(2026, 10, 22);

        await NewManager(repository)
            .SetSecRegistrantProfile(
                stock,
                "DE",
                "CA",
                "814-00755",
                null,
                null,
                null,
                new DateOnly(2026, 10, 22)
            );

        await repository.DidNotReceive().SaveChanges();
    }

    [Fact]
    public async Task SetSecRegistrantProfile_UnchangedProfileReadOnALaterDay_RecordsTheReadDate()
    {
        // The read date is what the refresh lane orders by, so it moves even when nothing else does.
        var repository = NewRepository();
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create();
        stock.StateOfIncorporation = "DE";
        stock.SecRegistrantProfileReadOn = new DateOnly(2026, 9, 1);

        await NewManager(repository)
            .SetSecRegistrantProfile(
                stock,
                "DE",
                null,
                null,
                null,
                null,
                null,
                new DateOnly(2026, 10, 22)
            );

        stock.SecRegistrantProfileReadOn.Should().Be(new DateOnly(2026, 10, 22));
        await repository.Received(1).SaveChanges();
    }
}
