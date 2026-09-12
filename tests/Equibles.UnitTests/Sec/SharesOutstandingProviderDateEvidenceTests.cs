using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.UnitTests.Sec;

public class SharesOutstandingProviderDateEvidenceTests
{
    [Fact]
    public async Task PriorYearContextInCurrentCover_DoesNotApplyOldSplitAgain()
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        var prior = Fact(
            stock,
            concept,
            12_176_027,
            new(2026, 5, 1),
            new(2026, 5, 8),
            new(2026, 3, 31)
        );
        var invalid = Fact(
            stock,
            concept,
            12_337_297,
            new(2025, 8, 3),
            new(2026, 8, 7),
            new(2026, 6, 30)
        );
        db.AddRange(
            prior,
            invalid,
            new StockSplit
            {
                CommonStockId = stock.Id,
                PriceSeriesTicker = stock.Ticker,
                EffectiveDate = new(2025, 10, 13),
                Numerator = 1,
                Denominator = 15,
            }
        );
        await db.SaveChangesAsync();

        var result = await Provider(db).GetCurrentSharesOutstanding(stock);

        Assert.Equal(12_176_027, result);
        Assert.Equal(new DateOnly(2025, 8, 3), invalid.PeriodEnd);
        Assert.Equal(12_337_297m, invalid.Value);
        Assert.Equal(EntityState.Unchanged, db.Entry(invalid).State);
    }

    [Theory]
    [InlineData("TenK")]
    [InlineData("TenKa")]
    [InlineData("TenQ")]
    [InlineData("TenQa")]
    [InlineData("TwentyF")]
    [InlineData("TwentyFa")]
    [InlineData("FortyF")]
    [InlineData("FortyFa")]
    public async Task ReportingForms_RejectPrePeriodCount(string form)
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        db.Add(
            Fact(
                stock,
                concept,
                100,
                new(2025, 12, 30),
                new(2026, 3, 1),
                new(2025, 12, 31),
                DocumentType.FromValue(form)
            )
        );
        await db.SaveChangesAsync();
        Assert.Null(await Provider(db).GetReportedSharesOutstanding(stock));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(31)]
    public async Task ReportAndFilingDateBoundaries_AreEligible(int day)
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        db.Add(Fact(stock, concept, 100, new(2026, 3, day), new(2026, 3, 31), new(2026, 3, 30)));
        await db.SaveChangesAsync();
        Assert.Equal(100, await Provider(db).GetReportedSharesOutstanding(stock));
    }

    [Fact]
    public async Task FutureCount_IsNotCurrentEvidence()
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        db.Add(Fact(stock, concept, 100, new(2027, 3, 31), new(2026, 5, 1), new(2026, 3, 31)));
        await db.SaveChangesAsync();
        Assert.Null(await Provider(db).GetReportedSharesOutstanding(stock));
    }

    [Fact]
    public async Task UnlinkedLegacyFact_RetainsItsSourceDate()
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        var fact = Fact(stock, concept, 100, new(2025, 3, 31), new(2026, 5, 1), new(2026, 3, 31));
        fact.Document = null;
        fact.DocumentId = null;
        db.Add(fact);
        await db.SaveChangesAsync();
        Assert.Equal(100, await Provider(db).GetReportedSharesOutstanding(stock));
    }

    [Fact]
    public async Task NonPeriodicFiling_DoesNotImposeAnInventedReportPeriod()
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        db.Add(
            Fact(
                stock,
                concept,
                100,
                new(2026, 3, 1),
                new(2026, 5, 1),
                new(2026, 5, 1),
                DocumentType.EightK
            )
        );
        await db.SaveChangesAsync();
        Assert.Equal(100, await Provider(db).GetReportedSharesOutstanding(stock));
    }

    [Theory]
    [InlineData(2025)]
    [InlineData(2027)]
    public async Task InvalidLatestClassDate_RejectsFilingRatherThanPublishingRemainingClass(
        int invalidYear
    )
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        var priorA = ClassFact(
            stock,
            concept,
            100,
            new(2025, 12, 31),
            new(2026, 2, 1),
            new(2025, 12, 31),
            "A"
        );
        var priorB = ClassFact(
            stock,
            concept,
            200,
            new(2025, 12, 31),
            new(2026, 2, 1),
            new(2025, 12, 31),
            "B"
        );
        var currentA = ClassFact(
            stock,
            concept,
            110,
            new(2026, 4, 1),
            new(2026, 5, 1),
            new(2026, 3, 31),
            "A"
        );
        var invalidB = ClassFact(
            stock,
            concept,
            210,
            new(invalidYear, 4, 1),
            new(2026, 5, 1),
            new(2026, 3, 31),
            "B"
        );
        db.AddRange(priorA, priorB, currentA, invalidB);
        await db.SaveChangesAsync();
        Assert.Equal(300, await Provider(db).GetSummedPerClassSharesOutstanding(stock));
    }

    [Theory]
    [InlineData("TwentyF")]
    [InlineData("FortyF")]
    public async Task RejectedCount_PreservesForeignFilingProtection(string form)
    {
        await using var db = NewDb();
        var (stock, concept) = SeedIdentity(db);
        db.AddRange(
            Fact(
                stock,
                concept,
                100,
                new(2026, 1, 1),
                new(2026, 2, 1),
                new(2026, 1, 1),
                DocumentType.SixK
            ),
            Fact(
                stock,
                concept,
                200,
                new(2025, 4, 1),
                new(2026, 5, 1),
                new(2026, 3, 31),
                DocumentType.FromValue(form)
            )
        );
        await db.SaveChangesAsync();
        var provider = Provider(db);
        Assert.Equal(100, await provider.GetReportedSharesOutstanding(stock));
        Assert.True(await provider.IsForeignPrivateIssuer(stock));
    }

    [Fact]
    public async Task ForeignProtection_PreservesCoverPagePriorityOverLaterBalanceSheet()
    {
        await using var db = NewDb();
        var (stock, cover) = SeedIdentity(db);
        var balance = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "CommonStockSharesOutstanding",
        };
        db.Add(balance);
        db.AddRange(
            Fact(
                stock,
                cover,
                100,
                new(2026, 1, 1),
                new(2026, 2, 1),
                new(2025, 12, 31),
                DocumentType.TwentyF
            ),
            Fact(
                stock,
                balance,
                200,
                new(2026, 4, 1),
                new(2026, 5, 1),
                new(2026, 3, 31),
                DocumentType.SixK
            )
        );
        await db.SaveChangesAsync();
        var provider = Provider(db);
        Assert.Equal(100, await provider.GetCurrentSharesOutstanding(stock));
        Assert.True(await provider.IsForeignPrivateIssuer(stock));
    }

    [Fact]
    public void DateFilter_TranslatesToPostgresWithSourceReportJoin()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseNpgsql("Host=localhost;Database=date_evidence;Username=test")
            .Options;
        using var db = new EquiblesFinancialDbContext(options, Modules());
        var sql = db.Set<FinancialFact>().Where(CurrentShareEvidenceDates.Eligible).ToQueryString();
        Assert.Contains("ReportingForDate", sql);
        Assert.Contains("FiledDate", sql);
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var db = new EquiblesFinancialDbContext(options, Modules());
        db.Database.EnsureCreated();
        return db;
    }

    private static IModuleConfiguration[] Modules() =>
        [
            new CommonStocksModuleConfiguration(),
            new FinancialFactsTestModuleConfiguration(),
            new CorporateActionsModuleConfiguration(),
        ];

    private static (CommonStock, FinancialConcept) SeedIdentity(EquiblesFinancialDbContext db)
    {
        var stock = new CommonStock
        {
            Ticker = "LPSN",
            Cik = "1102993",
            Name = "LivePerson",
        };
        var concept = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
        };
        db.AddRange(stock, concept);
        return (stock, concept);
    }

    private static SharesOutstandingProvider Provider(EquiblesFinancialDbContext db) =>
        new(
            new FinancialFactRepository(db),
            new FinancialConceptRepository(db),
            new StockSplitRepository(db)
        );

    private static FinancialFact Fact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly asOf,
        DateOnly filed,
        DateOnly report,
        DocumentType form = null
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            FinancialConceptId = concept.Id,
            Document = new Document
            {
                EquityIssuerId = stock.Id,
                DocumentType = form ?? DocumentType.TenQ,
                ReportingForDate = report,
                ReportingDate = filed,
            },
            Unit = "shares",
            PeriodType = FactPeriodType.Instant,
            PeriodStart = asOf,
            PeriodEnd = asOf,
            FiledDate = filed,
            Form = form ?? DocumentType.TenQ,
            AccessionNumber = filed.ToString("yyyyMMdd"),
            DimensionsKey = "",
            Value = value,
        };

    private static FinancialFact ClassFact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly asOf,
        DateOnly filed,
        DateOnly report,
        string member
    )
    {
        var fact = Fact(stock, concept, value, asOf, filed, report);
        const string axis = "us-gaap:StatementClassOfStockAxis";
        fact.DimensionsKey = $"{axis}={member}";
        fact.Dimensions.Add(new FinancialFactDimension { Axis = axis, Member = member });
        return fact;
    }
}
