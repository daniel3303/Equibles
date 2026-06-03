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

public class CommonStockManagerCreateDuplicateTickerTests
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
    public async Task Create_TickerAlreadyExistsForAnotherCompany_ThrowsDomainValidationException()
    {
        // Contract (CommonStockManager.cs:133): "Primary ticker must be globally
        // unique across all companies." Inserting a second company under a ticker
        // an existing company already holds must be rejected before persist. Other
        // Create tests throw on earlier guards (blank/negative) and never reach the
        // uniqueness check. A real repository is used so GetByPrimaryTicker runs the
        // actual lookup against the seeded row.
        var db = NewDb();
        db.Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Id = Guid.NewGuid(),
                    Ticker = "DUP",
                    Name = "Existing Co",
                    Cik = "0000000001",
                }
            );
        await db.SaveChangesAsync();

        var sut = new CommonStockManager(new CommonStockRepository(db), Substitute.For<IBus>());
        var incoming = new CommonStock
        {
            Ticker = "DUP",
            Name = "New Co",
            Cik = "0000000002",
        };

        var act = () => sut.Create(incoming);

        (await act.Should().ThrowAsync<DomainValidationException>()).WithMessage("*DUP*already*");
        // The incoming company was never persisted — only the pre-seeded row remains.
        db.Set<CommonStock>().Count(cs => cs.Cik == "0000000002").Should().Be(0);
    }
}
