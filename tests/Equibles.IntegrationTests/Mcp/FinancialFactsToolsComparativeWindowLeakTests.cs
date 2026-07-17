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
/// A filing re-reports comparative prior periods under its OWN fiscal
/// identity. A toDate window that excludes the reporting year's own period end
/// degenerates that (year, FY) group to the comparative alone, so per-group
/// picking emitted the SAME period end twice — once with the wrong FY label
/// and, in as-originally-reported mode, with a LATER filing's value. Live
/// repro: NVDA eps-diluted, form=10-K, toDate=2025-12-31 returned period end
/// 2025-01-26 as both "FY 2025" and "FY 2026".
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsToolsComparativeWindowLeakTests : ParadeDbMcpTestBase
{
    public FinancialFactsToolsComparativeWindowLeakTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private static CommonStock Nvidia() =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = "NVDA",
            Name = "NVIDIA Corp",
            Cik = "0001045810",
        };

    private void AddAnnualFact(
        CommonStock stock,
        FinancialConcept concept,
        int fiscalYearStamp,
        DateOnly periodStart,
        DateOnly periodEnd,
        decimal value,
        DateOnly filed,
        string accession
    )
    {
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = concept.Id,
                    Unit = "USD/shares",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    Value = value,
                    FiscalYear = fiscalYearStamp,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = filed,
                    AccessionNumber = accession,
                }
            );
    }

    private async Task<CommonStock> SeedNvdaEps()
    {
        var stock = Nvidia();
        DbContext.Set<CommonStock>().Add(stock);
        var eps = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "EarningsPerShareDiluted",
            Label = "EarningsPerShareDiluted",
        };
        DbContext.Set<FinancialConcept>().Add(eps);
        // FY2025 as filed by NVDA's own FY2025 10-K.
        AddAnnualFact(
            stock,
            eps,
            2025,
            new DateOnly(2024, 1, 29),
            new DateOnly(2025, 1, 26),
            2.94m,
            new DateOnly(2025, 2, 26),
            "nvda-fy2025"
        );
        // The FY2026 10-K: its own year...
        AddAnnualFact(
            stock,
            eps,
            2026,
            new DateOnly(2025, 1, 27),
            new DateOnly(2026, 1, 25),
            4.61m,
            new DateOnly(2026, 2, 25),
            "nvda-fy2026"
        );
        // ...plus the FY2025 comparative, re-stamped under the filing's own
        // fiscal identity (2026) — the leak's raw material.
        AddAnnualFact(
            stock,
            eps,
            2026,
            new DateOnly(2024, 1, 29),
            new DateOnly(2025, 1, 26),
            2.94m,
            new DateOnly(2026, 2, 25),
            "nvda-fy2026"
        );
        await DbContext.SaveChangesAsync();
        return stock;
    }

    [Fact]
    public async Task GetFinancialFact_ToDateExcludesOwnPeriodEnd_ComparativeDoesNotLeakAsExtraRow()
    {
        await SeedNvdaEps();

        var result = await Sut()
            .GetFinancialFact(
                "NVDA",
                "eps-diluted",
                form: "10-K",
                toDate: "2025-12-31",
                asOriginallyReported: true
            );

        // Exactly one row for period end 2025-01-26, labeled with ITS year.
        var occurrences = result.Split("| 2025-01-26 |").Length - 1;
        occurrences.Should().Be(1, "one reporting span is one row");
        result.Should().Contain("| 2025-01-26 | 2025 |");
        result
            .Should()
            .NotContain(
                "| 2025-01-26 | 2026 |",
                "the comparative's re-stamp is not a period of its own"
            );
    }

    [Fact]
    public async Task GetFinancialFact_NoWindow_BothYearsSurfaceOnce()
    {
        await SeedNvdaEps();

        var result = await Sut().GetFinancialFact("NVDA", "eps-diluted");

        result.Should().Contain("| 2026-01-25 | 2026 |");
        result.Should().Contain("| 2025-01-26 | 2025 |");
        (result.Split("| 2025-01-26 |").Length - 1).Should().Be(1);
    }

    [Fact]
    public async Task GetFinancialFact_FiscalPeriodFilter_ReturnsOnlyMatchingPeriods()
    {
        var stock = Nvidia();
        DbContext.Set<CommonStock>().Add(stock);
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        DbContext.Set<FinancialConcept>().Add(revenue);
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = revenue.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2025, 1, 27),
                    PeriodEnd = new DateOnly(2025, 4, 27),
                    Value = 44_000_000_000m,
                    FiscalYear = 2026,
                    FiscalPeriod = SecFiscalPeriod.Q1,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2025, 5, 28),
                    AccessionNumber = "nvda-q1",
                },
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = revenue.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2024, 1, 29),
                    PeriodEnd = new DateOnly(2025, 1, 26),
                    Value = 130_000_000_000m,
                    FiscalYear = 2025,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(2025, 2, 26),
                    AccessionNumber = "nvda-fy",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("NVDA", "revenue", fiscalPeriod: "FY");

        result.Should().Contain("$130,000,000,000");
        result.Should().NotContain("$44,000,000,000", "Q1 rows are filtered out");
    }

    [Fact]
    public async Task GetFinancialFact_UnknownFiscalPeriod_ReturnsGuidance()
    {
        var stock = Nvidia();
        DbContext.Set<CommonStock>().Add(stock);
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("NVDA", "revenue", fiscalPeriod: "H1");

        result.Should().Contain("Unknown fiscalPeriod 'H1'");
        result.Should().Contain("'FY' or 'Q1'..'Q4'");
    }

    [Fact]
    public async Task GetFinancialFact_MorePeriodsThanMaxResults_AppendsTruncationNote()
    {
        await SeedNvdaEps();

        var result = await Sut().GetFinancialFact("NVDA", "eps-diluted", maxResults: 1);

        result.Should().Contain("Showing first 1 of 2 results");
        result.Should().Contain("maxResults");
    }
}
