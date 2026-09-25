using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins that the stored-label read filters on the listed ticker with an index-servable shape. A
/// null-safe comparison forced a heap read of the iShares Trust filer's 2.4M rows and timed out.
/// </summary>
public class HoldingLabelConvergenceQueryTranslationTests
{
    [Fact]
    public void PrimaryLabelRead_FiltersWithIsNullOnly()
    {
        using var ctx = TranslationContext();

        var sql = HoldingLabelConvergenceService
            .BuildStoredLabelQuery(ctx, Guid.NewGuid(), null)
            .ToQueryString();

        sql.Should().Contain("\"ListedTicker\" IS NULL");
        sql.Should().NotContain(" OR ");
    }

    [Fact]
    public void PresentationLabelRead_FiltersWithEqualityOnly()
    {
        using var ctx = TranslationContext();

        var sql = HoldingLabelConvergenceService
            .BuildStoredLabelQuery(ctx, Guid.NewGuid(), "AAXJ")
            .ToQueryString();

        sql.Should().Contain("\"ListedTicker\" = ");
        sql.Should().NotContain("IS NULL");
        sql.Should().NotContain(" OR ");
    }

    [Fact]
    public void VacatedLabelRead_FiltersOnTheTickerListOnly()
    {
        using var ctx = TranslationContext();

        var sql = HoldingLabelConvergenceService
            .BuildVacatedLabelQuery(ctx, Guid.NewGuid(), ["SRG-PA"])
            .ToQueryString();

        sql.Should().Contain("\"ListedTicker\" = ANY");
        sql.Should().NotContain(" OR ");
    }

    private static EquiblesFinancialDbContext TranslationContext() =>
        new(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseNpgsql("Host=localhost;Database=translation-only")
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
            }
        );
}
