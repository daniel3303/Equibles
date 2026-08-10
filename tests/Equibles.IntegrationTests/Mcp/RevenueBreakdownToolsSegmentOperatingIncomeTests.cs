using System.Security.Cryptography;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Segment operating income (#7058) is rendered from the same business-segment axis as segment
/// revenue, but it obeys DIFFERENT rules, and getting that wrong states things about the issuer
/// that are not true:
/// <list type="bullet">
/// <item>its total row is operating income, not revenue — the shared renderer's hardcoded
/// "Total revenue (consolidated)" label would answer a revenue question with a profit figure;</item>
/// <item>segments are NOT expected to add up to the consolidated figure (unallocated corporate
/// costs sit outside them), so the revenue-shaped overlap warning would fire on a normal filing
/// and fabricate a claim about the issuer's XBRL tagging;</item>
/// <item>and because that same non-reconciliation makes the arithmetic completeness test
/// unpassable, a discontinued segment must be dropped by the newest filing's roster instead, or
/// it lingers forever and is summed into the table.</item>
/// </list>
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class RevenueBreakdownToolsSegmentOperatingIncomeTests : ParadeDbMcpTestBase
{
    private const string SegmentAxis = "us-gaap:StatementBusinessSegmentsAxis";

    public RevenueBreakdownToolsSegmentOperatingIncomeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private RevenueBreakdownTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<RevenueBreakdownTools>()
        );

    [Fact]
    public async Task GetRevenueBreakdown_SegmentOperatingIncome_LabelsItsOwnTotalAndSkipsTheRevenueOverlapWarning()
    {
        var stock = AddStock("AAPL");
        var revenue = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        // Consolidated revenue 416, segments sum to 416 — a clean revenue disaggregation.
        AddFact(stock, revenue, 2024, 416_000_000_000m);
        AddFact(
            stock,
            revenue,
            2024,
            250_000_000_000m,
            (SegmentAxis, "aapl:AmericasSegmentMember")
        );
        AddFact(stock, revenue, 2024, 166_000_000_000m, (SegmentAxis, "aapl:EuropeSegmentMember"));

        // Consolidated operating income 123, segments sum to 133 — a 8% overshoot that is NORMAL
        // for this axis, not evidence of overlapping tagging.
        AddFact(stock, operatingIncome, 2024, 123_000_000_000m);
        AddFact(
            stock,
            operatingIncome,
            2024,
            85_000_000_000m,
            (SegmentAxis, "aapl:AmericasSegmentMember")
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            48_000_000_000m,
            (SegmentAxis, "aapl:EuropeSegmentMember")
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("AAPL");

        result.Should().Contain("**Segment operating income**");
        result.Should().Contain("Total operating income (consolidated)");
        // The profit total must never be presented as revenue.
        result.Should().NotContain("| **Total revenue (consolidated)** | $123,000,000,000");
        // The revenue-shaped overlap claim must not be made about this axis.
        var operatingIncomeSection = result[
            result.IndexOf("**Segment operating income**", StringComparison.Ordinal)..
        ];
        operatingIncomeSection.Should().NotContain("Rows on this axis overlap");
        operatingIncomeSection.Should().Contain("need not add up to consolidated operating income");

        DumpForDocs(result);
    }

    [Fact]
    public async Task GetRevenueBreakdown_SegmentOperatingIncome_DropsASegmentTheNewestFilingNoLongerReports()
    {
        // The completeness defect this pins: on an axis that cannot reconcile arithmetically, the
        // old rule always fell back to a per-member carry-forward merge, so a segment the issuer
        // discontinued (NVDA Singapore, AMD Japan/Europe in the code's own examples) survived from
        // an older filing and was summed into every later period.
        var stock = AddStock("NVDA");
        var revenue = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        // The tool answers off the revenue cut, so a filer tagging segment operating income also
        // tags segment revenue; without it the whole response short-circuits before this axis.
        AddFact(stock, revenue, 2024, 60_000_000_000m);
        AddFact(stock, revenue, 2024, 35_000_000_000m, (SegmentAxis, "nvda:ComputeMember"));
        AddFact(stock, revenue, 2024, 25_000_000_000m, (SegmentAxis, "nvda:GraphicsMember"));

        AddFact(stock, operatingIncome, 2024, 30_000_000_000m);
        // Older filing reports three segments including one later discontinued.
        AddFact(
            stock,
            operatingIncome,
            2024,
            10_000_000_000m,
            1,
            [(SegmentAxis, "nvda:ComputeMember")]
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            8_000_000_000m,
            1,
            [(SegmentAxis, "nvda:GraphicsMember")]
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            2_000_000_000m,
            1,
            [(SegmentAxis, "nvda:SingaporeMember")]
        );
        // Newest filing restates the same period with the Singapore segment gone.
        AddFact(
            stock,
            operatingIncome,
            2024,
            11_000_000_000m,
            2,
            [(SegmentAxis, "nvda:ComputeMember")]
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            9_000_000_000m,
            2,
            [(SegmentAxis, "nvda:GraphicsMember")]
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("NVDA");

        result.Should().Contain("**Segment operating income**");
        result.Should().NotContain("Singapore");
        // The restated values from the newest filing are the ones shown.
        result.Should().Contain("$11,000,000,000");
    }

    // Writes the real rendering so a docs example is copied from tool output rather than written
    // by hand. Off unless explicitly requested, so the suite stays side-effect free in CI.
    private static void DumpForDocs(string result)
    {
        if (Environment.GetEnvironmentVariable("EQUIBLES_DUMP_DOCS_EXAMPLE") != "1")
            return;

        File.WriteAllText(
            Path.Combine(Path.GetTempPath(), "revenue-breakdown-docs-example.txt"),
            result
        );
    }

    private CommonStock AddStock(string ticker)
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = $"{ticker} Inc.",
            Cik = "0000320193",
        };
        DbContext.Set<CommonStock>().Add(stock);
        return stock;
    }

    private FinancialConcept AddConcept(string tag)
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

    private void AddFact(
        CommonStock stock,
        FinancialConcept concept,
        int fy,
        decimal value,
        params (string Axis, string Member)[] dimensions
    ) => AddFact(stock, concept, fy, value, 1, dimensions);

    private void AddFact(
        CommonStock stock,
        FinancialConcept concept,
        int fy,
        decimal value,
        int filedYearOffset,
        (string Axis, string Member)[] dimensions
    )
    {
        var fact = new FinancialFact
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            FinancialConceptId = concept.Id,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(fy, 1, 1),
            PeriodEnd = new DateOnly(fy, 12, 31),
            Value = value,
            FiscalYear = fy,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = new DateOnly(fy + filedYearOffset, 2, 1),
            AccessionNumber = $"acc-{Guid.NewGuid():N}"[..20],
            DimensionsKey = DimensionsKeyOf(dimensions),
        };
        foreach (var (axis, member) in dimensions)
            fact.Dimensions.Add(
                new FinancialFactDimension
                {
                    FinancialFactId = fact.Id,
                    Axis = axis,
                    Member = member,
                }
            );
        DbContext.Set<FinancialFact>().Add(fact);
    }

    private static string DimensionsKeyOf((string Axis, string Member)[] dimensions)
    {
        if (dimensions.Length == 0)
            return "";
        var canonical = string.Join(
            "|",
            dimensions
                .OrderBy(d => d.Axis)
                .ThenBy(d => d.Member)
                .Select(d => $"{d.Axis}={d.Member}")
        );
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}
