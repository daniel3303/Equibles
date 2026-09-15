using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the two partial indexes behind the daily repair scans to the queries they serve. A partial
/// index is used only when Postgres can prove the query's WHERE implies the index's predicate, so
/// the rendered SQL and the declared filter must agree word for word.
/// </summary>
public class HoldingsModuleRepairScanIndexTests
{
    private const string ImplausibleIndexName =
        "IX_InstitutionalHolding_ImplausibleDerivationRepair";
    private const string ImpossibleIndexName = "IX_InstitutionalHolding_ImpossiblePositionRepair";
    private const string IndexConcurrentAnnotation = "Npgsql:CreatedConcurrently";

    internal const string ImplausibleFilter =
        "NOT \"ValuePending\" AND \"ShareType\" = 0 AND \"ValueSource\" <> 1 AND \"Shares\" > 0 "
        + "AND \"Value\"::numeric > 1000000.0 * \"Shares\"::numeric";

    internal const string ImpossibleFilter =
        "\"ShareType\" = 0 AND NOT \"ValueUnavailable\" AND \"Shares\" > 1000000";

    [Fact]
    public void ImplausibleDerivationIndex_IsAConcurrentIdWorklist()
    {
        using var db = NewInMemoryDb();
        var index = db
            .Model.FindEntityType(typeof(InstitutionalHolding))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == ImplausibleIndexName);

        index.Properties.Select(p => p.Name).Should().Equal(nameof(InstitutionalHolding.Id));
        index.GetFilter().Should().Be(ImplausibleFilter);
        index.FindAnnotation(IndexConcurrentAnnotation)?.Value.Should().Be(true);
    }

    [Fact]
    public void ImpossiblePositionIndex_IsAConcurrentIssuerSharesIndexAboveTheFloor()
    {
        using var db = NewInMemoryDb();
        var index = db
            .Model.FindEntityType(typeof(InstitutionalHolding))!
            .GetIndexes()
            .Single(i => i.GetDatabaseName() == ImpossibleIndexName);

        index
            .Properties.Select(p => p.Name)
            .Should()
            .Equal(
                nameof(InstitutionalHolding.EquityIssuerId),
                nameof(InstitutionalHolding.Shares)
            );
        index.GetFilter().Should().Be(ImpossibleFilter);
        index.FindAnnotation(IndexConcurrentAnnotation)?.Value.Should().Be(true);
    }

    [Fact]
    public void ImpossiblePositionFloor_IsTheIndexPredicateLiteral()
    {
        ImpossibleFilter
            .Should()
            .EndWith(
                $"\"Shares\" > {ImpossiblePositionRepairService.CandidateSharesFloor}",
                "a batch query below the index's floor silently falls back to a full scan"
            );
    }

    [Fact]
    public void ImplausibleDerivationQuery_RendersTheIndexPredicateVerbatim()
    {
        using var db = NewTranslationDb();

        var sql = HoldingValueFallbackRepairService
            .BuildImplausibleDerivationCandidateQuery(db)
            .ToQueryString();

        WhereClause(sql).Should().Be(ImplausibleFilter);
        sql.Should().Contain("ORDER BY i.\"Id\"");
        sql.Should().Contain("LIMIT");
    }

    [Fact]
    public void IssuerAnchorQuery_NeverTouchesTheHoldingsTable()
    {
        using var db = NewTranslationDb();

        var sql = ImpossiblePositionRepairService.BuildIssuerAnchorQuery(db).ToQueryString();

        sql.Should().Contain("\"EquityIssuer\"");
        sql.Should().Contain("\"SharesOutstanding\" > 0");
        sql.Should().Contain("\"MarketCapitalization\" > 0");
        sql.Should().NotContain("InstitutionalHolding");
    }

    [Fact]
    public void CandidateBatchQuery_StaysInsideTheIndexAndProjectsColumnsOnly()
    {
        using var db = NewTranslationDb();

        var sql = ImpossiblePositionRepairService
            .BuildCandidateBatchQuery(
                db,
                [Guid.NewGuid()],
                ImpossiblePositionRepairService.CandidateSharesFloor
            )
            .ToQueryString();

        sql.Should().Contain("i.\"EquityIssuerId\" = ANY (");
        sql.Should().Contain("i.\"ShareType\" = 0");
        sql.Should().Contain("NOT (i.\"ValueUnavailable\")");
        sql.Should().Contain("i.\"Shares\" > @");
        sql.Should()
            .NotContain(
                "HoldingManagerEntry",
                "materialising the entity adds a second statement per batch"
            );
        sql.Should()
            .NotContain("EquityIssuer\"", "the issuer side is read once, not joined per batch");
        sql.Should().NotContain("JOIN");
    }

    // The rendered WHERE minus the table alias and the parenthesised NOT is what Postgres
    // compares against the index predicate; the alias and the parentheses are not nodes.
    private static string WhereClause(string sql)
    {
        var start = sql.IndexOf("WHERE ", StringComparison.Ordinal) + "WHERE ".Length;
        var end = sql.IndexOf("ORDER BY", start, StringComparison.Ordinal);
        return string.Join(
                ' ',
                sql[start..end].Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            )
            .Replace("i.\"", "\"")
            .Replace("NOT (\"ValuePending\")", "NOT \"ValuePending\"");
    }

    private static EquiblesFinancialDbContext NewTranslationDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only")
            .EnableServiceProviderCaching(false)
            .Options;
        return new EquiblesFinancialDbContext(options, Modules());
    }

    private static EquiblesFinancialDbContext NewInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new EquiblesFinancialDbContext(options, Modules());
    }

    private static IModuleConfiguration[] Modules() =>
        [
            new CommonStocksModuleConfiguration(),
            new HoldingsModuleConfiguration(),
            new CorporateActionsModuleConfiguration(),
        ];
}
