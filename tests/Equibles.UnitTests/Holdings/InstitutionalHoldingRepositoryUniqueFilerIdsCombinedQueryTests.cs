using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingRepositoryUniqueFilerIdsCombinedQueryTests
{
    [Fact]
    public void GetUniqueFilerIdsCombined_UsesDistinctTwoQuarterUnionWithoutCorrelatedProbe()
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

        var sql = repository
            .GetUniqueFilerIdsCombined(new DateOnly(2026, 6, 30), new DateOnly(2026, 3, 31))
            .ToQueryString();

        sql.Should().Contain("SELECT DISTINCT");
        sql.ToUpperInvariant().Should().NotContain("NOT EXISTS");
    }
}
