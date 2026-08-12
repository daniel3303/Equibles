using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingRepositoryHolderActivityQueryTests
{
    [Fact]
    public void Get13FHolderActivityByStock_AggregatesBothQuartersInSql()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        using var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
        var repository = new InstitutionalHoldingRepository(context);
        var stock = new Equibles.CommonStocks.Data.Models.CommonStock { Id = Guid.NewGuid() };

        var sql = repository
            .Get13FHolderActivityByStock(
                stock,
                new DateOnly(2026, 6, 30),
                new DateOnly(2026, 3, 31)
            )
            .ToQueryString();

        sql.Should().Contain("GROUP BY");
        sql.Should().Contain("ListedTicker");
        sql.Should().Contain("CASE");
        sql.ToUpperInvariant().Should().Contain("SUM");
        sql.ToUpperInvariant().Should().Contain("COUNT");
        sql.Should().NotContain("JOIN");
    }
}
