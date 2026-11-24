using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// A filer that reports its first quarter, then only six-month, nine-month and full-year figures:
/// the series lists every quarter, the second to fourth worked out from that year and marked.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsToolsDerivedQuartersTests : ParadeDbMcpTestBase
{
    public FinancialFactsToolsDerivedQuartersTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private async Task Seed()
    {
        var revenue = Concept("Revenues");
        var operatingExpenses = Concept("OperatingExpenses");
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "HALF",
            Name: "Half Year Reporter",
            Cik: "0000999001"
        );
        DbContext.Set<EquityIssuer>().Add(issuer);
        foreach (var concept in new[] { revenue, operatingExpenses })
        {
            Add(issuer, concept, "2025-03-31", 30m, SecFiscalPeriod.Q1, DocumentType.TenQ);
            Add(issuer, concept, "2025-06-30", 55m, SecFiscalPeriod.Q2, DocumentType.TenQ);
            Add(issuer, concept, "2025-09-30", 85m, SecFiscalPeriod.Q3, DocumentType.TenQ);
            Add(issuer, concept, "2025-12-31", 120m, SecFiscalPeriod.FullYear, DocumentType.TenK);
        }
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFinancialFact_CumulativeOnlyQuarters_ListsEachQuarterMarkedDerived()
    {
        await Seed();

        var result = await Sut().GetFinancialFact("HALF", "revenue");

        result
            .Should()
            .Contain("| 2025-01-01 | 2025-03-31 | 2025 | Q1 | $30,000,000 | USD | 10-Q |");
        result
            .Should()
            .Contain("| 2025-04-01 | 2025-06-30 | 2025 | Q2 | $25,000,000 (derived) | USD |");
        result
            .Should()
            .Contain("| 2025-07-01 | 2025-09-30 | 2025 | Q3 | $30,000,000 (derived) | USD |");
        result
            .Should()
            .Contain("| 2025-10-01 | 2025-12-31 | 2025 | Q4 | $35,000,000 (derived) | USD |");
        result
            .Should()
            .Contain("| 2025-01-01 | 2025-12-31 | 2025 | FY | $120,000,000 | USD | 10-K |");
        result
            .Should()
            .Contain("(derived): a quarter the filer reports only inside a cumulative figure");

        var fourth = await Sut().GetFinancialFact("HALF", "revenue", fiscalPeriod: "Q4");
        fourth
            .Should()
            .Contain("| 2025-10-01 | 2025-12-31 | 2025 | Q4 | $35,000,000 (derived) | USD |");
    }

    // The next year's quarterly reports restate the 2025 comparatives (first quarter 33, half-year
    // 61); 2025's own quarters still derive from 2025's own filings, as the first quarter shown does.
    [Fact]
    public async Task GetFinancialFact_LaterYearRestatesTheComparatives_DerivesFromTheYearsOwnFilings()
    {
        await Seed();
        var issuer = DbContext.Set<EquityIssuer>().Local.Single();
        var revenue = DbContext.Set<FinancialConcept>().Local.Single(c => c.Tag == "Revenues");
        Add(
            issuer,
            revenue,
            "2025-03-31",
            33m,
            SecFiscalPeriod.Q1,
            DocumentType.TenQ,
            2026,
            "2026-05-10"
        );
        Add(
            issuer,
            revenue,
            "2025-06-30",
            61m,
            SecFiscalPeriod.Q2,
            DocumentType.TenQ,
            2026,
            "2026-08-10"
        );
        Add(
            issuer,
            revenue,
            "2026-03-31",
            40m,
            SecFiscalPeriod.Q1,
            DocumentType.TenQ,
            2026,
            "2026-05-10",
            start: "2026-01-01"
        );
        Add(
            issuer,
            revenue,
            "2026-06-30",
            80m,
            SecFiscalPeriod.Q2,
            DocumentType.TenQ,
            2026,
            "2026-08-10",
            start: "2026-01-01"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("HALF", "revenue");

        result
            .Should()
            .Contain("| 2025-04-01 | 2025-06-30 | 2025 | Q2 | $25,000,000 (derived) | USD |");
        result
            .Should()
            .Contain("| 2025-07-01 | 2025-09-30 | 2025 | Q3 | $30,000,000 (derived) | USD |");
        result
            .Should()
            .Contain("| 2025-10-01 | 2025-12-31 | 2025 | Q4 | $35,000,000 (derived) | USD |");
        result
            .Should()
            .Contain("| 2026-04-01 | 2026-06-30 | 2026 | Q2 | $40,000,000 (derived) | USD |");
    }

    [Fact]
    public async Task GetFinancialFact_FiledFiguresOrANonAdditiveLine_DerivesNothing()
    {
        await Seed();

        var filed = await Sut().GetFinancialFact("HALF", "revenue", form: "10-Q");
        var original = await Sut().GetFinancialFact("HALF", "revenue", asOriginallyReported: true);
        var mixedLine = await Sut().GetFinancialFact("HALF", "operating-expenses");

        foreach (var result in new[] { filed, original, mixedLine })
        {
            result.Should().NotContain("(derived)");
            result.Should().NotContain("| Q2 |");
        }
    }

    private FinancialConcept Concept(string tag)
    {
        var concept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = tag,
            Label = tag,
        };
        DbContext.Set<FinancialConcept>().Add(concept);
        return concept;
    }

    private void Add(
        EquityIssuer issuer,
        FinancialConcept concept,
        string end,
        decimal millions,
        SecFiscalPeriod period,
        DocumentType form,
        int fiscalYear = 2025,
        string filed = null,
        string start = "2025-01-01"
    )
    {
        var periodEnd = DateOnly.Parse(end);
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = issuer.Id,
                    FinancialConceptId = concept.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = DateOnly.Parse(start),
                    PeriodEnd = periodEnd,
                    Value = millions * 1_000_000m,
                    FiscalYear = fiscalYear,
                    FiscalPeriod = period,
                    Form = form,
                    FiledDate = filed == null ? periodEnd.AddDays(40) : DateOnly.Parse(filed),
                    AccessionNumber = $"0000999001-{fiscalYear % 100}-{period}-{periodEnd:yyMMdd}",
                }
            );
    }
}
