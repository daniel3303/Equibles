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
// facts on the class-of-stock axis, no consolidated fact) — has its classes summed into the
// entity total instead of a single class being mistaken for it (#2503).
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
        db.Add(
            Fact(
                stock,
                concept,
                276_898_105m,
                new DateOnly(2025, 11, 25),
                new DateOnly(2025, 11, 25)
            )
        );
        // Latest filing — the current post-reverse-split entity total.
        db.Add(
            Fact(stock, concept, 14_061_261m, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 29))
        );
        // A per-class dimensional fact in the same latest filing must never be chosen.
        db.Add(
            Fact(
                stock,
                concept,
                99_999m,
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 5, 29),
                dimensionsKey: "dei:SecurityClassAxis=copr:ClassAMember"
            )
        );
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
        db.Add(
            Fact(
                stock,
                concept,
                5_800_000_000m,
                new DateOnly(2026, 4, 24),
                new DateOnly(2026, 4, 17),
                dimensionsKey: "dei:SecurityClassAxis=goog:ClassAMember"
            )
        );
        db.Add(
            Fact(
                stock,
                concept,
                6_400_000_000m,
                new DateOnly(2026, 4, 24),
                new DateOnly(2026, 4, 17),
                dimensionsKey: "dei:SecurityClassAxis=goog:ClassCMember"
            )
        );
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetReportedSharesOutstanding(stock);

        shares.Should().BeNull();
    }

    [Fact]
    public async Task GetSummedPerClassSharesOutstanding_SumsLatestFilingAcrossShareClasses()
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
        // An older filing whose per-class counts must be ignored entirely.
        db.Add(
            ClassFact(
                stock,
                concept,
                5_671_000_000m,
                new(2026, 1, 31),
                new(2026, 1, 24),
                "0001652044-26-000018",
                "us-gaap:CommonClassAMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                5_438_000_000m,
                new(2026, 1, 31),
                new(2026, 1, 24),
                "0001652044-26-000018",
                "goog:CapitalClassCMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                837_000_000m,
                new(2026, 1, 31),
                new(2026, 1, 24),
                "0001652044-26-000018",
                "us-gaap:CommonClassBMember"
            )
        );
        // The latest filing: Class A + Class C + Class B = 12,116,000,000 (the entity total).
        db.Add(
            ClassFact(
                stock,
                concept,
                5_824_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                "0001652044-26-000048",
                "us-gaap:CommonClassAMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                5_456_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                "0001652044-26-000048",
                "goog:CapitalClassCMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                836_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                "0001652044-26-000048",
                "us-gaap:CommonClassBMember"
            )
        );
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetSummedPerClassSharesOutstanding(stock);

        shares.Should().Be(12_116_000_000);
    }

    [Fact]
    public async Task GetSummedPerClassSharesOutstanding_OnlyConsolidatedFact_ReturnsNull()
    {
        await using var db = NewDb();
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
        };
        db.AddRange(stock, concept);
        // Single-class issuer: only a consolidated fact, no per-class dimensional facts — summing
        // has nothing to sum, so the consolidated path (GetReportedSharesOutstanding) is used.
        db.Add(
            Fact(
                stock,
                concept,
                14_687_356_000m,
                new DateOnly(2026, 5, 1),
                new DateOnly(2026, 4, 26)
            )
        );
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetSummedPerClassSharesOutstanding(stock);

        shares.Should().BeNull();
    }

    [Fact]
    public async Task GetSummedPerClassSharesOutstanding_IgnoresNonClassAxisDimensionalFacts()
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
        // A dimensional fact on a non-class axis must never be summed as a share class — only
        // genuine per-class counts on the class-of-stock axis count toward the entity total.
        db.Add(
            ClassFact(
                stock,
                concept,
                5_824_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                "0001652044-26-000048",
                "aapl:IPhoneMember",
                axis: "srt:ProductOrServiceAxis"
            )
        );
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetSummedPerClassSharesOutstanding(stock);

        shares.Should().BeNull();
    }

    [Fact]
    public async Task GetSummedPerClassSharesOutstanding_DeduplicatesRestatedClassRow_InLatestFiling()
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
        // The latest filing carries Class A twice (a restated/duplicate row). The contract pins each
        // class is counted once, so the entity total must not double-count Class A.
        const string acc = "0001652044-26-000048";
        db.Add(
            ClassFact(
                stock,
                concept,
                5_824_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                acc,
                "us-gaap:CommonClassAMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                5_824_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                acc,
                "us-gaap:CommonClassAMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                5_456_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                acc,
                "goog:CapitalClassCMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                836_000_000m,
                new(2026, 4, 30),
                new(2026, 4, 23),
                acc,
                "us-gaap:CommonClassBMember"
            )
        );
        await db.SaveChangesAsync();

        var provider = new SharesOutstandingProvider(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db)
        );

        var shares = await provider.GetSummedPerClassSharesOutstanding(stock);

        shares.Should().Be(12_116_000_000);
    }

    private static FinancialFact ClassFact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly filed,
        DateOnly asOf,
        string accession,
        string member,
        string axis = "us-gaap:StatementClassOfStockAxis"
    )
    {
        var fact = new FinancialFact
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
            AccessionNumber = accession,
            DimensionsKey = $"{axis}={member}",
        };
        fact.Dimensions.Add(new FinancialFactDimension { Axis = axis, Member = member });
        return fact;
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
