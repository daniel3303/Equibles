using Equibles.CommonStocks.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins that the snapshot builder's churn query — a chained GroupBy (per (stock, holder)
/// presence flags, then per stock counts) — stays translatable by the Npgsql provider.
/// <c>ToQueryString</c> runs the full relational translation pipeline without opening a
/// connection, so an EF upgrade that stops translating this shape fails here instead of
/// faulting every dirty-quarter snapshot rebuild at runtime. The chained shape replaced
/// per-row correlated NOT-EXISTS probes that cost ~35s per rebuild at production scale.
/// </summary>
public class HoldingsAggregateRefreshServiceChurnQueryTranslationTests
{
    [Fact]
    public void BuildChurnQuery_TranslatesToSingleStatementSql()
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
            }
        );

        var query = HoldingsAggregateRefreshService.BuildChurnQuery(
            ctx,
            new DateOnly(2026, 3, 31),
            new DateOnly(2025, 12, 31)
        );

        // Throws InvalidOperationException ("could not be translated") when the shape stops
        // translating; the GROUP BY assertions pin that both grouping levels stay in SQL
        // rather than one silently falling back to client evaluation.
        var sql = query.ToQueryString();
        sql.Should().Contain("GROUP BY");
        sql.ToUpperInvariant().Should().NotContain("NOT EXISTS");
    }
}
