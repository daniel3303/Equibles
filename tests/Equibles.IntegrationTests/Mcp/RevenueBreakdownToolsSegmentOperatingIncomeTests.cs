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
/// <item>a partial amendment may restate only one member, so unchanged members must carry forward;
/// a newest-filing roster is authoritative only when its members reconcile as a complete
/// re-disaggregation.</item>
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
        result.Should().Contain("**Segment operating margin** (%)");
        result.Should().Contain("| Americas Segment | 34 |");
        result.Should().Contain("same folded raw member QName and exact period match");
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
    public async Task GetRevenueBreakdown_IncomeWithoutDimensionalRevenue_StillRendersIncome()
    {
        var stock = AddStock("SOLO");
        var revenue = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        AddFact(stock, revenue, 2024, 100_000_000m);
        AddFact(stock, operatingIncome, 2024, 20_000_000m);
        AddFact(stock, operatingIncome, 2024, 12_000_000m, (SegmentAxis, "solo:CoreMember"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("SOLO");

        result.Should().Contain("No dimensional revenue tagging is on record");
        result.Should().Contain("**Segment operating income**");
        result.Should().Contain("| Core | $12,000,000 |");
        result.Should().NotContain("**Segment operating margin**");
        result.Should().NotContain("has no dimensional revenue or segment operating income");
    }

    [Fact]
    public async Task GetRevenueBreakdown_IncomeWithoutAnyRevenueConcept_StillRendersIncome()
    {
        var stock = AddStock("INCOMEONLY");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        AddFact(stock, operatingIncome, 2024, 20_000_000m);
        AddFact(stock, operatingIncome, 2024, 12_000_000m, (SegmentAxis, "incomeonly:CoreMember"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("INCOMEONLY");

        result.Should().Contain("No dimensional revenue tagging is on record");
        result.Should().Contain("**Segment operating income**");
        result.Should().Contain("| Core | $12,000,000 |");
        result.Should().NotContain("**Segment operating margin**");
        result.Should().NotContain("has no dimensional revenue or segment operating income");
    }

    [Fact]
    public async Task GetRevenueBreakdown_MultiYearDurationsNeverRenderAsAnnualSegments()
    {
        var stock = AddStock("SPAN");
        var revenue = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        AddFact(stock, revenue, 2024, 100m);
        AddFact(stock, revenue, 2024, 60m, (SegmentAxis, "span:CurrentMember"));
        AddFact(stock, operatingIncome, 2024, 20m);
        AddFact(stock, operatingIncome, 2024, 12m, (SegmentAxis, "span:CurrentMember"));

        AddFactWithSpan(
            stock,
            revenue,
            new DateOnly(2020, 1, 1),
            new DateOnly(2025, 12, 31),
            7_777m
        );
        AddFactWithSpan(
            stock,
            revenue,
            new DateOnly(2020, 1, 1),
            new DateOnly(2025, 12, 31),
            9_999m,
            (SegmentAxis, "span:InceptionToDateMember")
        );
        AddFactWithSpan(
            stock,
            operatingIncome,
            new DateOnly(2020, 1, 1),
            new DateOnly(2025, 12, 31),
            6_666m
        );
        AddFactWithSpan(
            stock,
            operatingIncome,
            new DateOnly(2020, 1, 1),
            new DateOnly(2025, 12, 31),
            8_888m,
            (SegmentAxis, "span:InceptionToDateMember")
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("SPAN");

        result.Should().Contain("Current");
        result.Should().NotContain("Inception To Date");
        result.Should().NotContain("2025-12-31");
        result.Should().NotContain("7,777");
        result.Should().NotContain("6,666");
        result.Should().NotContain("9,999");
        result.Should().NotContain("8,888");
    }

    [Fact]
    public async Task GetRevenueBreakdown_SegmentOperatingIncome_CarriesUnchangedMembersAcrossPartialAmendment()
    {
        // A partial amendment can restate one segment without repeating every unchanged member.
        // Treating that one-row filing as the whole roster silently deletes valid segments.
        var stock = AddStock("NVDA");
        var revenue = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        // Seed matching segment revenue so this case also exercises the derived-margin path.
        AddFact(stock, revenue, 2024, 60_000_000_000m);
        AddFact(stock, revenue, 2024, 35_000_000_000m, (SegmentAxis, "nvda:ComputeMember"));
        AddFact(stock, revenue, 2024, 25_000_000_000m, (SegmentAxis, "nvda:GraphicsMember"));

        AddFact(stock, operatingIncome, 2024, 30_000_000_000m);
        // Original filing reports all three segments.
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
        // Amendment restates Compute alone. Its $11B cannot reconcile to the $30B consolidated
        // figure, proving that this is not a complete re-disaggregation.
        AddFact(
            stock,
            operatingIncome,
            2024,
            11_000_000_000m,
            2,
            [(SegmentAxis, "nvda:ComputeMember")]
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("NVDA");

        result.Should().Contain("**Segment operating income**");
        result.Should().Contain("Singapore");
        result.Should().Contain("$11,000,000,000");
        result.Should().Contain("$8,000,000,000");
        result.Should().Contain("$2,000,000,000");
    }

    [Fact]
    public async Task GetRevenueBreakdown_SegmentOperatingIncome_DropsOldMemberAfterCompleteRedisaggregation()
    {
        var stock = AddStock("DROP");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        AddFact(stock, operatingIncome, 2024, 20_000_000_000m);
        AddFact(
            stock,
            operatingIncome,
            2024,
            10_000_000_000m,
            1,
            [(SegmentAxis, "drop:ComputeMember")]
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            8_000_000_000m,
            1,
            [(SegmentAxis, "drop:GraphicsMember")]
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            2_000_000_000m,
            1,
            [(SegmentAxis, "drop:SingaporeMember")]
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            11_000_000_000m,
            2,
            [(SegmentAxis, "drop:ComputeMember")]
        );
        AddFact(
            stock,
            operatingIncome,
            2024,
            9_000_000_000m,
            2,
            [(SegmentAxis, "drop:GraphicsMember")]
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("DROP");

        result.Should().Contain("$11,000,000,000");
        result.Should().Contain("$9,000,000,000");
        result.Should().NotContain("Singapore");
    }

    [Fact]
    public async Task GetRevenueBreakdown_MergesMemberQNameSpellingDriftAcrossYearsBeforeBuildingMargins()
    {
        var stock = AddStock("AMD");
        var revenue = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        var operatingIncome = AddConcept("OperatingIncomeLoss");

        AddFact(stock, revenue, 2023, 100m);
        AddFact(stock, revenue, 2023, 60m, (SegmentAxis, "amd:DatacenterMember"));
        AddFact(stock, revenue, 2023, 40m, (SegmentAxis, "amd:ClientMember"));
        AddFact(stock, revenue, 2024, 200m);
        AddFact(stock, revenue, 2024, 120m, (SegmentAxis, "amd:DataCenterMember"));
        AddFact(stock, revenue, 2024, 80m, (SegmentAxis, "amd:ClientMember"));

        AddFact(stock, operatingIncome, 2023, 25m);
        AddFact(stock, operatingIncome, 2023, 15m, (SegmentAxis, "amd:DatacenterMember"));
        AddFact(stock, operatingIncome, 2023, 10m, (SegmentAxis, "amd:ClientMember"));
        AddFact(stock, operatingIncome, 2024, 50m);
        AddFact(stock, operatingIncome, 2024, 30m, (SegmentAxis, "amd:DataCenterMember"));
        AddFact(stock, operatingIncome, 2024, 20m, (SegmentAxis, "amd:ClientMember"));
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("AMD");
        var marginSection = result[
            result.IndexOf("**Segment operating margin**", StringComparison.Ordinal)..
        ];

        marginSection.Should().Contain("| Component | 2023-12-31 | 2024-12-31 |");
        marginSection.Should().Contain("| Data Center | 25 | 25 |");
        marginSection.Should().NotContain("| Datacenter |");
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

    private void AddFactWithSpan(
        CommonStock stock,
        FinancialConcept concept,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal value,
        params (string Axis, string Member)[] dimensions
    )
    {
        var fact = new FinancialFact
        {
            Id = Guid.NewGuid(),
            CommonStockId = stock.Id,
            FinancialConceptId = concept.Id,
            Unit = "USD",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Value = value,
            FiscalYear = periodEnd.Year,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = periodEnd.AddDays(45),
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
