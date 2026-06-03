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

public class CommonStockManagerCreateDuplicateCikTests
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

    [Fact]
    public async Task Create_CikAlreadyExistsForAnotherCompany_ThrowsDomainValidationException()
    {
        // Contract (CommonStockManager.cs:142-147): a CIK is the SEC filer identity
        // and must not be claimed by two companies. This guard sits AFTER the ticker
        // uniqueness check, so the incoming ticker is deliberately unique — the only
        // collision is the CIK. No existing test reaches it. A real repository is used
        // so GetByCik runs the actual lookup against the seeded row.
        var db = NewDb();
        db.Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Id = Guid.NewGuid(),
                    Ticker = "AAA",
                    Name = "Existing Co",
                    Cik = "0000000005",
                }
            );
        await db.SaveChangesAsync();

        var sut = new CommonStockManager(new CommonStockRepository(db), Substitute.For<IBus>());
        var incoming = new CommonStock
        {
            Ticker = "BBB",
            Name = "New Co",
            Cik = "0000000005",
        };

        var act = () => sut.Create(incoming);

        (await act.Should().ThrowAsync<DomainValidationException>()).WithMessage(
            "*cik*0000000005*already*"
        );
        // The incoming company was never persisted — only the pre-seeded row remains.
        db.Set<CommonStock>().Count(cs => cs.Ticker == "BBB").Should().Be(0);
    }
}
