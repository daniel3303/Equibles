using System.Text.RegularExpressions;
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
        Regex.Matches(sql, "\"Ticker\" = @listedTicker").Should().HaveCount(2);
    }

    // A membership test over each issuer's reference tickers (`IN (SELECT ... WHERE issuer)`)
    // walked the whole directory per call; the reference branch alone must stay the flat join
    // that starts from the ticker index.
    [Fact]
    public void GetUsReferenceTickerOwnerIds_IsOneFlatJoinOnTheListingTicker()
    {
        using var context = NewContext();
        var repository = new EquityIssuerRepository(context);

        var sql = repository.GetUsReferenceTickerOwnerIds("SPY").ToQueryString();

        sql.Should().NotContain("UNION");
        sql.Should().NotContain(" OR ");
        sql.Should().NotContain(" IN (");
        sql.Should().Contain("\"IsReferenceListed\"");
        Regex.Matches(sql, "\"Ticker\" = @listedTicker").Should().HaveCount(1);
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
