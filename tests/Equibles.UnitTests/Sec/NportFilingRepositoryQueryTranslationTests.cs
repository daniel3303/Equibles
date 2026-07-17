using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Sec.Data;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the SQL shapes that keep the NPORT reverse-holdings path plannable. Both queries were
/// observed at multiple seconds per call at production scale before being restructured
/// (~9.5 s per stock-summary render): the CUSIP lookup OR-ed a constant with an uncorrelated
/// subquery (which defeats the CUSIP index and degrades to a full scan of the 25M-row holdings
/// table), and the latest-per-series dedup OR-ed its registrant-population identity legs into
/// one anti-join condition (no hashable key → an O(N²) nested loop over every filing pair).
/// <c>ToQueryString</c> runs the full Npgsql translation pipeline offline, so an EF upgrade or
/// refactor that regresses either shape fails here instead of on every stock page render.
/// </summary>
public class NportFilingRepositoryQueryTranslationTests
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
                new MediaModuleConfiguration(),
                new SecModuleConfiguration(),
            }
        );
    }

    [Fact]
    public void GetHoldingsByStockCusip_TranslatesToASingleInSemiJoin_NeverAnOr()
    {
        using var ctx = CreateContext();
        var repository = new NportFilingRepository(ctx);

        var sql = repository
            .GetHoldingsByStockCusip(
                new CommonStock { Id = Guid.NewGuid(), Cusip = "037833100" }
            )
            .ToQueryString();

        // The current CUSIP must ride inside the alias subquery (UNION), never as a separate
        // OR-ed predicate — "Cusip = $1 OR Cusip IN (subquery)" is unplannable via the index.
        sql.ToUpperInvariant().Should().Contain("UNION");
        sql.ToUpperInvariant().Should().NotContain(" OR ");
    }

    [Fact]
    public void GetLatestPerSeries_TranslatesToPartitionedBranches_WithHashableIdentityKeys()
    {
        using var ctx = CreateContext();
        var repository = new NportFilingRepository(ctx);

        var sql = repository.GetLatestPerSeries(new DateOnly(2024, 1, 1)).ToQueryString();
        var flat = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");

        // One branch per registrant population, concatenated — never one OR-ed identity.
        System.Text.RegularExpressions.Regex
            .Matches(flat, "UNION ALL")
            .Count.Should()
            .Be(2);

        // The trust branch's anti-join must keep a bare RegistrantCik equality: EF null
        // compensation ("= OR both-null") would strip the anti-join of its hash key and
        // regress it to the O(N²) nested loop. The in-lambda "f.RegistrantCik != null"
        // guard is what keeps the compensation out — pin that it worked.
        flat.Should()
            .NotMatchRegex(
                "\"RegistrantCik\" = \\w+\\.\"RegistrantCik\"\\)? OR",
                "the RegistrantCik anti-join key must stay a plain hashable equality"
            );
    }
}
