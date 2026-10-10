using Equibles.CommonStocks.Data;
using Equibles.CorporateActions.Data;
using Equibles.Data;
using Equibles.Holdings.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins that the retry ladder's due check travels to the database. The unresolved-pair pass once
/// loaded every pending row of a pair, with its manager legs, only to skip the ones whose day had
/// not come; the InMemory harness evaluates any shape client-side, so only a translation pin can
/// catch the predicate falling back to the client.
/// </summary>
public class HoldingsValueRecalculatorRetryDueTranslationTests
{
    [Fact]
    public void RetryDue_TranslatesEveryLadderStepToSql()
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
        var now = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        var sql = ctx.Set<InstitutionalHolding>()
            .Where(h => h.ValuePending)
            .Where(HoldingsValueRecalculator.RetryDue(now))
            .ToQueryString();

        // RetryDue carries one branch per ladder step; a fourth delay needs a fourth branch.
        HoldingsValueRecalculator.RetryDelays.Should().HaveCount(3);
        var where = sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..];
        where.Should().Contain("\"ValueRetryCount\" = 0");
        where.Should().Contain("\"ValueRetryCount\" = 1");
        where.Should().Contain("\"ValueRetryCount\" >= 2");
        where.Should().Contain("COALESCE(i.\"ValueLastRetryAt\", i.\"CreationTime\") <= @");
        // The three anchors travel as parameters: the ladder's 1, 7 and 30 days before now.
        sql.Should().Contain("2026-10-09");
        sql.Should().Contain("2026-10-03");
        sql.Should().Contain("2026-09-10");
    }
}
