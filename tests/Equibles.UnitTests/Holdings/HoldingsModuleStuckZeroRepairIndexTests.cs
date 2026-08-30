using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

public class HoldingsModuleStuckZeroRepairIndexTests
{
    private const string IndexName = "IX_InstitutionalHolding_StuckZeroRepair";
    private const string IndexConcurrentAnnotation = "Npgsql:CreatedConcurrently";

    [Fact]
    public void StuckZeroRepairIndex_IsASmallConcurrentWorklist()
    {
        using var db = NewDb();

        var entity = db.Model.FindEntityType(typeof(InstitutionalHolding));
        entity.Should().NotBeNull();

        var index = entity.GetIndexes().Single(i => i.GetDatabaseName() == IndexName);

        index.Properties.Select(p => p.Name).Should().Equal(nameof(InstitutionalHolding.Id));
        index
            .GetFilter()
            .Should()
            .Be(
                "\"Value\" = 0 AND NOT \"ValuePending\" AND NOT \"ValueUnavailable\" "
                    + "AND \"FiledValue\" IS NOT NULL AND \"FiledValue\" > 0"
            );
        index
            .FindAnnotation(IndexConcurrentAnnotation)
            ?.Value.Should()
            .Be(true, "the 72 GB holdings table must stay writable while the index builds");
    }

    [Fact]
    public void StuckZeroCandidateQuery_MatchesThePartialIndexPredicateAndOrdering()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        using var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );

        var sql = HoldingValueFallbackRepairService
            .BuildStuckZeroCandidateQuery(db)
            .ToQueryString();

        sql.Should().Contain("WHERE i.\"Value\" = 0");
        sql.Should().Contain("NOT (i.\"ValuePending\")");
        sql.Should().Contain("NOT (i.\"ValueUnavailable\")");
        sql.Should().Contain("i.\"FiledValue\" IS NOT NULL");
        sql.Should().Contain("i.\"FiledValue\" > 0");
        sql.Should().Contain("ORDER BY i.\"Id\"");
        sql.Should().Contain("LIMIT");
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new HoldingsModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
            }
        );
    }
}
