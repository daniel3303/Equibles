using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins that the filed-publish revision candidate query stays translatable by the Npgsql
/// provider. The frontier is expressed as <c>Guid.CompareTo</c> (Guid has no comparison
/// operators in LINQ), a shape the InMemory-backed integration tests would happily evaluate
/// client-side — so without this pin an EF upgrade that stops translating it would pass every
/// test and fault the repair phase at runtime instead.
/// </summary>
public class HoldingValueFallbackRepairServiceReviseQueryTranslationTests
{
    [Fact]
    public void BuildReviseCandidateQuery_TranslatesToSql()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        using var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );

        var query = HoldingValueFallbackRepairService.BuildReviseCandidateQuery(
            ctx,
            Guid.NewGuid()
        );

        // Throws InvalidOperationException ("could not be translated") when the shape stops
        // translating; the assertions pin that the frontier comparison and the row cap stay in
        // SQL rather than silently falling back to client evaluation.
        var sql = query.ToQueryString();
        sql.Should().Contain("LIMIT");
        sql.Should().Contain(">");
    }
}
