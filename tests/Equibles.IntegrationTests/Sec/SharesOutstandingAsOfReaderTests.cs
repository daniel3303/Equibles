using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.TestSupport;

namespace Equibles.IntegrationTests.Sec;

// The per-class read correlates facts with their dimension set by key, which only real Postgres runs.
[Collection(ParadeDbCollection.Name)]
public class SharesOutstandingAsOfReaderTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private const string ClassAxis = "us-gaap:StatementClassOfStockAxis";
    private static readonly DateOnly AsOf = new(2026, 4, 20);

    private FinancialFact CoverCount(
        EquityIssuer issuer,
        FinancialConcept cover,
        decimal shares,
        params (string Axis, string Member)[] dimensions
    ) =>
        new()
        {
            EquityIssuerId = issuer.Id,
            FinancialConceptId = cover.Id,
            Unit = "shares",
            Value = shares,
            PeriodType = FactPeriodType.Instant,
            PeriodStart = AsOf,
            PeriodEnd = AsOf,
            FiscalYear = 2026,
            FiscalPeriod = SecFiscalPeriod.Q1,
            Form = DocumentType.TenQ,
            FiledDate = new DateOnly(2026, 4, 30),
            AccessionNumber = "0001326801-26-000040",
            DimensionsKey = FinancialDimensionSeed.Key(DbContext, dimensions),
        };

    [Fact]
    public async Task Read_PerClassCounts_SumsEachClassAndSkipsNonClassAndMultiAxisFacts()
    {
        var issuer = EquityIssuerSeed.Create(Id: Guid.NewGuid(), Ticker: "META");
        var cover = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
            Label = "Entity Common Stock, Shares Outstanding",
        };
        DbContext.AddRange(issuer, cover);
        await DbContext.SaveChangesAsync();
        DbContext.AddRange(
            CoverCount(issuer, cover, 2_180m, (ClassAxis, "us-gaap:CommonClassAMember")),
            CoverCount(issuer, cover, 340m, (ClassAxis, "us-gaap:CommonClassBMember")),
            // A class member cut by a second axis is not a per-class cover count.
            CoverCount(
                issuer,
                cover,
                9_000m,
                (ClassAxis, "us-gaap:CommonClassAMember"),
                ("dei:LegalEntityAxis", "meta:SubsidiaryMember")
            )
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var counts = await new SharesOutstandingAsOfReader(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext)
        ).Read(AsOf.AddDays(30));

        counts[issuer.Id]
            .Should()
            .Be(new SharesOutstandingAsOf(2_520, AsOf, IsClassSum: true, IsForeignForm: false));
    }
}
