using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Repositories;
using Equibles.CommonStocks.Repositories.Extensions;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Media.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.CommonStocks;

public class CommonStockRepositoryTickerOwnerQueryTests
{
    // An OR across the default and reference listings made PostgreSQL walk every directory
    // issuer; each union branch must compare one listing's ticker directly.
    [Fact]
    public void GetUsTickerOwnerIds_FiltersEachListingTickerInItsOwnUnionBranch()
    {
        using var context = NewContext();
        var repository = new EquityIssuerRepository(context);

        var sql = repository.GetUsTickerOwnerIds("AAPL").ToQueryString();

        sql.Should().Contain("UNION");
        sql.Should().NotContain(" OR ");
        sql.Split("\"Ticker\" = @listedTicker").Should().HaveCount(3);
    }

    private static EquiblesFinancialDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new MediaModuleConfiguration(),
            }
        );
    }
}
