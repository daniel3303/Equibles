using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
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

// Pins the split restatement in GetCurrentSharesOutstanding: a cover-page count is stated as-of
// a date inside its filing window, so a captured split effective AFTER that date must rescale it
// — otherwise every market-cap and ownership surface stays on the pre-split basis until the
// issuer's next cover page, a whole quarter away (BYND's 1-for-30 served a ~$6.2B market cap on
// a ~$210M company). The first post-split cover page makes the restatement a natural no-op.
public class SharesOutstandingProviderSplitRestatementTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

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
                new CorporateActionsModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static SharesOutstandingProvider NewProvider(EquiblesFinancialDbContext db) =>
        new(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db),
            new StockSplitRepository(db)
        );

    private static CommonStock Stock() =>
        new()
        {
            Ticker = "BYND",
            Name = "Beyond Meat",
            Cik = "0001655210",
        };

    private static FinancialConcept CoverPageConcept() =>
        new() { Taxonomy = FactTaxonomy.Dei, Tag = "EntityCommonStockSharesOutstanding" };

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

    private static FinancialFact ClassFact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly filed,
        DateOnly asOf,
        string member
    )
    {
        const string axis = "us-gaap:StatementClassOfStockAxis";
        var fact = Fact(stock, concept, value, filed, asOf, dimensionsKey: $"{axis}={member}");
        fact.AccessionNumber = "ACC-SAME-FILING";
        fact.Dimensions.Add(new FinancialFactDimension { Axis = axis, Member = member });
        return fact;
    }

    private static StockSplit Split(
        CommonStock stock,
        DateOnly effective,
        decimal numerator,
        decimal denominator,
        string priceSeriesTicker
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            EffectiveDate = effective,
            Numerator = numerator,
            Denominator = denominator,
            Source = StockSplitSource.External,
            PriceSeriesTicker = priceSeriesTicker,
        };

    [Fact]
    public async Task ReverseSplitEffectiveAfterTheFactsAsOfDate_RestatesTheCount()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        // The BYND shape: 10-Q filed Aug 6 with the count as of Aug 1; 1-for-30 effective Aug 14.
        db.Add(Fact(stock, concept, 515_818_978m, Today.AddDays(-12), Today.AddDays(-17)));
        db.Add(Split(stock, Today.AddDays(-4), 1m, 30m, "BYND"));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(17_193_966); // 515,818,978 / 30, rounded
    }

    [Fact]
    public async Task FactStatedOnTheEffectiveDate_IsAlreadyPostSplit_NotRestated()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        var effective = Today.AddDays(-4);
        // The first post-split cover page: stated ON the effective date or later — restating it
        // again would double-apply, so the strict after-AsOf comparison must exclude it.
        db.Add(Fact(stock, concept, 17_200_000m, Today.AddDays(-1), effective));
        db.Add(Split(stock, effective, 1m, 30m, "BYND"));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(17_200_000);
    }

    [Fact]
    public async Task AnnouncedFutureSplit_DoesNotRestateAnything()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        db.Add(Fact(stock, concept, 515_818_978m, Today.AddDays(-12), Today.AddDays(-17)));
        // Captured at announcement, effective next week — today's count must not move.
        db.Add(Split(stock, Today.AddDays(7), 1m, 30m, "BYND"));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(515_818_978);
    }

    [Fact]
    public async Task SplitAttributedToASiblingSeries_DoesNotRescaleTheEntityCount()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        db.Add(Fact(stock, concept, 515_818_978m, Today.AddDays(-12), Today.AddDays(-17)));
        db.Add(Split(stock, Today.AddDays(-4), 1m, 30m, "BYND-B"));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(515_818_978);
    }

    [Fact]
    public async Task LegacyNullAttributedSplit_RestatesLikeThePrimarySeries()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        db.Add(Fact(stock, concept, 100_000_000m, Today.AddDays(-12), Today.AddDays(-17)));
        // Forward 2:1 with legacy null series attribution: the count doubles.
        db.Add(Split(stock, Today.AddDays(-4), 2m, 1m, null));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(200_000_000);
    }

    [Fact]
    public async Task PerClassSum_IsRestatedByTheSameRule()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        db.Add(
            ClassFact(
                stock,
                concept,
                200_000_000m,
                Today.AddDays(-12),
                Today.AddDays(-17),
                "bynd:ClassAMember"
            )
        );
        db.Add(
            ClassFact(
                stock,
                concept,
                100_000_000m,
                Today.AddDays(-12),
                Today.AddDays(-17),
                "bynd:ClassBMember"
            )
        );
        db.Add(Split(stock, Today.AddDays(-4), 1m, 30m, "BYND"));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(10_000_000); // (200M + 100M) / 30
    }

    [Fact]
    public async Task CorruptSplitRatio_LeavesTheCountAsFiled()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        db.Add(Fact(stock, concept, 515_818_978m, Today.AddDays(-12), Today.AddDays(-17)));
        // A zero numerator would zero the count — worse than the stale figure; leave it as filed.
        db.Add(Split(stock, Today.AddDays(-4), 0m, 30m, "BYND"));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(515_818_978);
    }

    [Fact]
    public async Task GetReportedSharesOutstanding_StaysAsFiled()
    {
        await using var db = NewDb();
        var stock = Stock();
        var concept = CoverPageConcept();
        db.AddRange(stock, concept);
        db.Add(Fact(stock, concept, 515_818_978m, Today.AddDays(-12), Today.AddDays(-17)));
        db.Add(Split(stock, Today.AddDays(-4), 1m, 30m, "BYND"));
        await db.SaveChangesAsync();

        // The as-reported accessor documents the filing verbatim; only the "current entity
        // total" contract restates onto today's basis.
        var shares = await NewProvider(db).GetReportedSharesOutstanding(stock);

        shares.Should().Be(515_818_978);
    }
}
