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

[Collection(ParadeDbCollection.Name)]
public class RevenueBreakdownToolsTests : ParadeDbMcpTestBase
{
    public RevenueBreakdownToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private RevenueBreakdownTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<RevenueBreakdownTools>()
        );

    private FinancialConcept AddConcept(string tag)
    {
        var c = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = tag,
            Label = tag,
        };
        DbContext.Set<FinancialConcept>().Add(c);
        return c;
    }

    private void AddDimensionalFact(
        CommonStock stock,
        FinancialConcept concept,
        int fy,
        decimal value,
        DateOnly filed,
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
            PeriodStart = new DateOnly(fy, 1, 1),
            PeriodEnd = new DateOnly(fy, 12, 31),
            Value = value,
            FiscalYear = fy,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = filed,
            AccessionNumber = $"acc-{Guid.NewGuid():N}"[..20],
            // Production stores the SHA-256 of the ordinal-sorted dimensions (varchar(64)),
            // not the raw QName pairs; mirror that so the value fits the column.
            DimensionsKey = DimensionsKeyOf(dimensions),
        };
        foreach (var (axis, member) in dimensions)
        {
            fact.Dimensions.Add(
                new FinancialFactDimension
                {
                    FinancialFactId = fact.Id,
                    Axis = axis,
                    Member = member,
                }
            );
        }
        DbContext.Set<FinancialFact>().Add(fact);
    }

    private static string DimensionsKeyOf((string Axis, string Member)[] dimensions)
    {
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

    // Issuers like Apple tag operating-segment revenue with the srt:ConsolidationItemsAxis
    // qualifier from FY2025 on (segment × ConsolidationItems=OperatingSegments). The single-
    // dimension filter used to drop those facts, so the latest fiscal year vanished from the
    // segment axis (#3628). The qualifier must count as a single-axis segment cut, while any
    // other ConsolidationItems member (corporate, eliminations) stays excluded.
    [Fact]
    public async Task GetRevenueBreakdown_OperatingSegmentsQualifier_SurfacesLatestYear()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "OPSEG",
            Name = "Operating Segments Corp.",
            Cik = "0009830003",
        };
        DbContext.Set<CommonStock>().Add(stock);
        var asc606 = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");

        // FY2024 — Cloud reported single-axis (old-style filing).
        AddDimensionalFact(
            stock,
            asc606,
            2024,
            100m,
            new DateOnly(2025, 2, 1),
            ("us-gaap:StatementBusinessSegmentsAxis", "opseg:CloudMember")
        );
        // FY2025 — operating segments carry the OperatingSegments qualifier.
        AddDimensionalFact(
            stock,
            asc606,
            2025,
            130m,
            new DateOnly(2026, 2, 1),
            ("us-gaap:StatementBusinessSegmentsAxis", "opseg:CloudMember"),
            ("srt:ConsolidationItemsAxis", "us-gaap:OperatingSegmentsMember")
        );
        AddDimensionalFact(
            stock,
            asc606,
            2025,
            70m,
            new DateOnly(2026, 2, 1),
            ("us-gaap:StatementBusinessSegmentsAxis", "opseg:HardwareMember"),
            ("srt:ConsolidationItemsAxis", "us-gaap:OperatingSegmentsMember")
        );
        // FY2025 — a corporate/non-segment cut on the same axis must never surface.
        AddDimensionalFact(
            stock,
            asc606,
            2025,
            25m,
            new DateOnly(2026, 2, 1),
            ("us-gaap:StatementBusinessSegmentsAxis", "opseg:CorporateMember"),
            ("srt:ConsolidationItemsAxis", "us-gaap:CorporateNonSegmentMember")
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("OPSEG");

        result.Should().Contain("By segment");
        result
            .Should()
            .Contain(
                "2025-12-31",
                "the OperatingSegments-qualified FY2025 facts surface on the segment axis"
            );
        result.Should().Contain("Cloud");
        result.Should().Contain("Hardware");
        result
            .Should()
            .NotContain("Corporate", "the corporate/non-segment cut is not an operating segment");
    }
}
