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
/// Issuers tag overlapping granularity levels on one axis — Apple's
/// Product/Service parents alongside iPhone/Mac/iPad/Wearables, NVDA's Data
/// Center alongside Compute + Networking. The disjoint-scheme collapse can
/// never fire on those shapes (the schemes share or nest members), so the
/// table's rows sum to ~2x revenue. Each axis table must therefore carry the
/// consolidated total row (shares become computable, double-counting becomes
/// visible) and an explicit overlap caution when the rows overshoot it.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class RevenueBreakdownToolsTotalRowAndOverlapTests : ParadeDbMcpTestBase
{
    private const string ProductAxis = "srt:ProductOrServiceAxis";

    public RevenueBreakdownToolsTotalRowAndOverlapTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private RevenueBreakdownTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<RevenueBreakdownTools>()
        );

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

    private void AddFact(
        CommonStock stock,
        FinancialConcept concept,
        int fy,
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
            PeriodStart = new DateOnly(fy, 1, 1),
            PeriodEnd = new DateOnly(fy, 12, 31),
            Value = value,
            FiscalYear = fy,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TenK,
            FiledDate = new DateOnly(fy + 1, 2, 1),
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

    [Fact]
    public async Task GetRevenueBreakdown_NestedParentAlongsideComponents_TotalRowAndOverlapCaution()
    {
        var stock = AddStock("AAPL");
        var revenue = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        // Consolidated total: 416.
        AddFact(stock, revenue, 2024, 416_000_000_000m);
        // Parent scheme: Product + Service = 416.
        AddFact(stock, revenue, 2024, 307_000_000_000m, (ProductAxis, "us-gaap:ProductMember"));
        AddFact(stock, revenue, 2024, 109_000_000_000m, (ProductAxis, "us-gaap:ServiceMember"));
        // Product's components — they nest under (and share Service with) the
        // parent scheme, so no disjoint full-total partition exists.
        AddFact(stock, revenue, 2024, 200_000_000_000m, (ProductAxis, "aapl:IPhoneMember"));
        AddFact(stock, revenue, 2024, 40_000_000_000m, (ProductAxis, "aapl:MacMember"));
        AddFact(stock, revenue, 2024, 30_000_000_000m, (ProductAxis, "aapl:IPadMember"));
        AddFact(
            stock,
            revenue,
            2024,
            37_000_000_000m,
            (ProductAxis, "aapl:WearablesHomeandAccessoriesMember")
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("AAPL");

        result.Should().Contain("Total revenue (consolidated)");
        result.Should().Contain("$416,000,000,000");
        result.Should().Contain("Rows on this axis overlap");
        result.Should().Contain("must NOT be summed");
        result
            .Should()
            .Contain("shown exactly as the issuer tags them", "the rename caveat is stated");
        result.Should().Contain("IPhone").And.NotContain("I Phone");
    }

    [Fact]
    public async Task GetRevenueBreakdown_SingleCleanScheme_TotalRowButNoOverlapCaution()
    {
        var stock = AddStock("CLEAN");
        var revenue = AddConcept("Revenues");
        AddFact(stock, revenue, 2024, 100_000_000_000m);
        AddFact(
            stock,
            revenue,
            2024,
            60_000_000_000m,
            ("us-gaap:StatementBusinessSegmentsAxis", "clean:CloudMember")
        );
        AddFact(
            stock,
            revenue,
            2024,
            40_000_000_000m,
            ("us-gaap:StatementBusinessSegmentsAxis", "clean:HardwareMember")
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("CLEAN");

        result.Should().Contain("Total revenue (consolidated)");
        result.Should().Contain("$100,000,000,000");
        result.Should().NotContain("Rows on this axis overlap");
    }

    [Fact]
    public async Task GetRevenueBreakdown_MoreYearsThanMaxYears_AppendsTruncationNote()
    {
        var stock = AddStock("YEARS");
        var revenue = AddConcept("Revenues");
        foreach (var fy in new[] { 2022, 2023, 2024 })
        {
            AddFact(stock, revenue, fy, 100m);
            AddFact(
                stock,
                revenue,
                fy,
                100m,
                ("us-gaap:StatementBusinessSegmentsAxis", "years:OnlyMember")
            );
        }
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetRevenueBreakdown("YEARS", maxYears: 2);

        result.Should().Contain("Showing the latest 2 of 3 fiscal years");
        result.Should().Contain("raise maxYears");
    }
}
