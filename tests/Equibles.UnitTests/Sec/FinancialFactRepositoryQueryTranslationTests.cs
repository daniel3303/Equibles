using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Sec.Data;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins that the two queries dating a balance sheet reach SQL at all, with the shape that
/// keeps them on the [CommonStockId, FiscalYear, FiscalPeriod] index. <c>ToQueryString</c>
/// runs the full Npgsql translation offline, so a provider or refactor that stops translating
/// the granularity predicate fails here instead of on every statement render.
/// </summary>
public class FinancialFactRepositoryQueryTranslationTests
{
    private static EquiblesFinancialDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=translation-only", o => o.UseVector())
            .EnableServiceProviderCaching(false)
            .Options;
        return new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecModuleConfiguration(),
                new FinancialFactsModuleConfiguration(),
            }
        );
    }

    private static readonly CommonStock Stock = new() { Id = Guid.NewGuid(), Ticker = "HD" };

    [Fact]
    public void GetMeasuredFlows_FullYear_TranslatesTheAnnualBoundsAgainstThePeriodStart()
    {
        using var ctx = CreateContext();
        var repository = new FinancialFactRepository(ctx);

        var where = WhereClause(
            repository
                .GetMeasuredFlows(Stock, 2025, SecFiscalPeriod.FullYear, [Guid.NewGuid()])
                .ToQueryString()
        );

        where
            .Should()
            .Contain("\"DimensionsKey\" = ''", "only the consolidated flows date a sheet");
        where.Should().Contain("\"FiscalYear\" = @");
        where.Should().Contain("\"FiscalPeriod\" = @");
        where
            .Should()
            .Contain(
                "\"FinancialConceptId\" = ANY (@flowConceptIds)",
                "only a flow-statement concept may end the period; without this any consolidated span would"
            );
        // Both annual bounds ride against PeriodStart (a Postgres date plus an integer is that
        // many days later), so the span rule is the one the in-memory gate applies, 350..380
        // days, and never a plain PeriodEnd range.
        where.Should().Contain("\"PeriodEnd\" >= f.\"PeriodStart\" + 350");
        where.Should().Contain("\"PeriodEnd\" <= f.\"PeriodStart\" + 380");
    }

    [Fact]
    public void GetMeasuredFlows_Quarter_TranslatesTheDiscreteQuarterBounds()
    {
        using var ctx = CreateContext();
        var repository = new FinancialFactRepository(ctx);

        var where = WhereClause(
            repository
                .GetMeasuredFlows(Stock, 2025, SecFiscalPeriod.Q2, [Guid.NewGuid()])
                .ToQueryString()
        );

        where.Should().Contain("\"PeriodEnd\" >= f.\"PeriodStart\" + 1 ");
        where.Should().Contain("\"PeriodEnd\" <= f.\"PeriodStart\" + 100");
        where.Should().NotContain("350", "a quarter bucket is never gated by the annual bounds");
    }

    [Fact]
    public void GetStatedNear_TranslatesToConsolidatedPointsInADateWindow_FromAnyFiscalStamp()
    {
        using var ctx = CreateContext();
        var repository = new FinancialFactRepository(ctx);

        var where = WhereClause(
            repository
                .GetStatedNear(Stock, [Guid.NewGuid()], new DateOnly(2025, 2, 2), 7)
                .ToQueryString()
        );

        where
            .Should()
            .Contain("\"DimensionsKey\" = ''", "a segment's instant is not the company's");
        where.Should().Contain("\"PeriodEnd\" = f.\"PeriodStart\"", "only a point states a date");
        where.Should().Contain("\"PeriodEnd\" >= @");
        where.Should().Contain("\"PeriodEnd\" <= @");
        where
            .Should()
            .NotContain(
                "\"FiscalYear\"",
                "the sheet is loaded from whichever bucket holds it; the stamp is the defect"
            );
        where.Should().NotContain("\"FiscalPeriod\"");
        where.Should().NotContain(" OR ");
    }

    // The predicate alone: the projection lists every column, stamp included.
    private static string WhereClause(string sql)
    {
        var flat = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");
        var at = flat.IndexOf(" WHERE ", StringComparison.Ordinal);
        at.Should().BeGreaterThan(-1, "the query must filter");
        return flat[at..] + " ";
    }
}
