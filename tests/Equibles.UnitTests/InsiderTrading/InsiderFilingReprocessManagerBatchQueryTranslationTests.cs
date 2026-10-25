using Equibles.Data;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Media.Data;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.InsiderTrading;

/// <summary>
/// Pins the reprocess batch as one GROUP BY level under its LIMIT. PostgreSQL plans a DISTINCT
/// subquery for full retrieval, so production hashed every stale row of the version for
/// every 32-filing batch instead of streaming the (ParserVersion, AccessionNumber) index.
/// </summary>
public class InsiderFilingReprocessManagerBatchQueryTranslationTests
{
    [Fact]
    public void NextAccessionBatch_GroupsAtTheLimitLevel_NeverADistinctSubquery()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .Options;
        using var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new InsiderTradingModuleConfiguration(),
                new MediaModuleConfiguration(),
            }
        );
        var pending = db.Set<InsiderTransaction>()
            .IgnoreQueryFilters()
            .Where(t =>
                t.TransactionCode != TransactionCode.IngestMarker
                && t.ParserVersion < InsiderTransaction.CurrentParserVersion
            );

        var sql = InsiderFilingReprocessManager.NextAccessionBatch(pending, 12).ToQueryString();

        sql.Should().NotContain("DISTINCT");
        sql.Should().NotContain("FROM (");
        sql.Should().Contain("GROUP BY i.\"AccessionNumber\"");
        sql.Should().Contain("ORDER BY i.\"AccessionNumber\"");
        sql.Should().Contain("LIMIT");
    }
}
