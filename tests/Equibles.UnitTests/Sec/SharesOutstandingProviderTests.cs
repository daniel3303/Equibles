using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.UnitTests.Sec;

// Pins SharesOutstandingProvider: the entity share count comes from the latest-filed CONSOLIDATED
// dei:EntityCommonStockSharesOutstanding cover-page fact (so a reverse split is reflected at once,
// #3575), and a multi-class issuer — which reports the count only per share class (dimensional
// facts) — yields null so a single class is never mistaken for the entity total (#2503).
public class SharesOutstandingProviderTests
{
    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new FinancialFactsTestModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    [Fact]
    public async Task GetReportedSharesOutstanding_PicksLatestFiledConsolidatedFact_IgnoringDimensional()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "COPR",
            Name = "Idaho Copper",
            Cik = "0001263364",
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
        };
        db.AddRange(stock, concept);
        // Older filing — the stale pre-split figure Yahoo also carries.
        db.Add(Fact(stock, concept, 276_898_105m, new DateOnly(2025, 11, 25), new DateOnly(2025, 11, 25)));
        // Latest filing — the current post-reverse-split entity total.
        db.Add(Fact(stock, concept, 14_061_261m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 29)));
        // A per-class dimensional fact in the same latest filing must never be chosen.
        db.Add(Fact(stock, concept, 99_999m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 29),
            dimensionsKey: "dei:SecurityClassAxis=copr:ClassAMember"));
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetReportedSharesOutstanding(stock);

        shares.Should().Be(14_061_261);
    }

    [Fact]
    public async Task GetReportedSharesOutstanding_OnlyDimensionalPerClassFacts_ReturnsNull()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "GOOGL",
            Name = "Alphabet",
            Cik = "0001652044",
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
        };
        db.AddRange(stock, concept);
        // Multi-class issuer: the cover-page count is reported only per share class (dimensional),
        // so there is no consolidated fact for the entity total — that is summed across classes
        // separately, not sourced here.
        db.Add(Fact(stock, concept, 5_800_000_000m, new DateOnly(2026, 4, 24), new DateOnly(2026, 4, 17),
            dimensionsKey: "dei:SecurityClassAxis=goog:ClassAMember"));
        db.Add(Fact(stock, concept, 6_400_000_000m, new DateOnly(2026, 4, 24), new DateOnly(2026, 4, 17),
            dimensionsKey: "dei:SecurityClassAxis=goog:ClassCMember"));
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetReportedSharesOutstanding(stock);

        shares.Should().BeNull();
    }

    private static FinancialFact Fact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly filed,
        DateOnly asOf,
        string dimensionsKey = ""
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            FinancialConceptId = concept.Id,
            Unit = "shares",
            PeriodType = FactPeriodType.Instant,
            PeriodStart = asOf,
            PeriodEnd = asOf,
            Value = value,
            FiscalYear = asOf.Year,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenQ,
            FiledDate = filed,
            AccessionNumber = $"ACC-{Guid.NewGuid():N}"[..20],
            DimensionsKey = dimensionsKey,
        };
}
