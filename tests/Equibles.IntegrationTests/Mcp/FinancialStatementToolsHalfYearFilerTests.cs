using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

// HSBC's recorded shape: a 20-F full year, then a 6-K half year whose income flows are six-month spans
// stamped Q2 with no Q1 to subtract, so no discrete second quarter exists.
[Collection(ParadeDbCollection.Name)]
public class FinancialStatementToolsHalfYearFilerTests : ParadeDbMcpTestBase
{
    public FinancialStatementToolsHalfYearFilerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialStatementTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

    [Fact]
    public async Task DefaultIncomeStatement_HalfYearOnlyQuarter_ShowsTheLatestFullYearAndNamesTheSkippedPeriod()
    {
        await SeedHsbc();

        var result = await Sut().GetFinancialStatement("HSBC", statement: "income");

        result.Should().Contain("Income Statement for HSBC (Hsbc Holdings PLC) — FY2025 FY:");
        result
            .Should()
            .Contain("| Net Income | $23,131,000,000 | USD | Reported | 2025-01-01 | 2025-12-31 |");
        result.Should().Contain("| Pretax Income | $29,907,000,000 |");
        result
            .Should()
            .Contain(
                "_FY2026 Q2 is newer but has no income statement this tool can show, so the newest "
                    + "period that has one is shown._"
            );
        result
            .Should()
            .NotContain("15,321,000,000", "a six-month span is never shown as a quarter");
    }

    [Fact]
    public async Task ExplicitHalfYearQuarter_SaysItsFlowsAreYearToDate()
    {
        await SeedHsbc();

        var result = await Sut()
            .GetFinancialStatement("HSBC", statement: "income", year: 2026, period: "Q2");

        result
            .Should()
            .Be(
                "HSBC has no discrete-quarter income statement line items for FY2026 Q2: its flows are "
                    + "cumulative spans from which no discrete quarter can be formed. Use period 'FY' for "
                    + "annual figures, or GetFinancialFact for one cumulative line."
            );
    }

    // The half-year balance sheet is a real point-in-time statement, so the default keeps it.
    [Fact]
    public async Task DefaultBalanceSheet_HalfYearInstants_StillDefaultsToTheHalfYear()
    {
        await SeedHsbc();

        var result = await Sut().GetFinancialStatement("HSBC", statement: "balance");

        result.Should().Contain("Balance Sheet for HSBC (Hsbc Holdings PLC) — FY2026 Q2:");
        result.Should().Contain("| Total Assets | $3,438,161,000,000 |");
        result.Should().NotContain("is newer but has no");
    }

    // A quarterly filer's newest Q2 renders by subtracting Q1 from the six-month cash flow, so the
    // default stays on it with no note.
    [Fact]
    public async Task DefaultCashFlow_NewestQuarterDerivedFromYearToDate_StaysOnItWithoutANote()
    {
        var filer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "QTRF",
            Name: "Quarterly Filer Inc",
            Cik: "0000000777"
        );
        var operating = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "NetCashProvidedByUsedInOperatingActivities",
            Label = "Operating cash flow",
        };
        var tenQ = (DocumentType.TenQ, new DateOnly(2026, 8, 1), "0000000777-26-000002");
        DbContext.Set<EquityIssuer>().Add(filer);
        DbContext.Set<FinancialConcept>().Add(operating);
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                Fact(
                    filer,
                    operating,
                    2026,
                    SecFiscalPeriod.Q1,
                    new(2026, 1, 1),
                    new(2026, 3, 31),
                    400m,
                    tenQ,
                    "USD"
                ),
                Fact(
                    filer,
                    operating,
                    2026,
                    SecFiscalPeriod.Q2,
                    new(2026, 1, 1),
                    new(2026, 6, 30),
                    1_000m,
                    tenQ,
                    "USD"
                )
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialStatement("QTRF", statement: "cashflow");

        result.Should().Contain("— FY2026 Q2:");
        result
            .Should()
            .Contain(
                "| Operating Cash Flow | $600 | USD | Derived quarter | 2026-04-01 | 2026-06-30 |"
            );
        result.Should().NotContain("is newer but");
    }

    // A period-only request may take an older year's quarter, and says which newer one it passed over.
    [Fact]
    public async Task PeriodOnlyRequest_FallsBackToTheNewestYearWhoseQuarterRenders()
    {
        await SeedHsbc();
        var profit = await DbContext
            .Set<FinancialConcept>()
            .SingleAsync(c => c.Tag == "ProfitLoss");
        var hsbc = await DbContext
            .Set<EquityIssuer>()
            .SingleAsync(i => i.Name == "Hsbc Holdings PLC");
        DbContext
            .Set<FinancialFact>()
            .Add(
                Fact(
                    hsbc,
                    profit,
                    2025,
                    SecFiscalPeriod.Q2,
                    new(2025, 4, 1),
                    new(2025, 6, 30),
                    6_000_000_000m,
                    HalfYear
                )
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialStatement("HSBC", statement: "income", period: "Q2");

        result.Should().Contain("— FY2025 Q2:").And.Contain("| Net Income | $6,000,000,000 |");
        result
            .Should()
            .Contain("_FY2026 Q2 is newer but has no income statement this tool can show");
    }

    [Fact]
    public async Task PeriodOnlyRequest_EveryCandidateEmpty_ReturnsTheNewestPeriodsReason()
    {
        await SeedHsbc();

        var result = await Sut().GetFinancialStatement("HSBC", statement: "income", period: "Q2");

        result
            .Should()
            .StartWith("HSBC has no discrete-quarter income statement line items for FY2026 Q2:");
    }

    private async Task SeedHsbc()
    {
        var hsbc = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "HSBC",
            Name: "Hsbc Holdings PLC",
            Cik: "0001089113"
        );
        var profit = Ifrs("ProfitLoss");
        var pretax = Ifrs("ProfitLossBeforeTax");
        var assets = Ifrs("Assets");
        DbContext.Set<EquityIssuer>().Add(hsbc);
        DbContext.Set<FinancialConcept>().AddRange(profit, pretax, assets);
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                Fact(
                    hsbc,
                    profit,
                    2025,
                    SecFiscalPeriod.FullYear,
                    new(2025, 1, 1),
                    new(2025, 12, 31),
                    23_131_000_000m,
                    FullYear
                ),
                Fact(
                    hsbc,
                    pretax,
                    2025,
                    SecFiscalPeriod.FullYear,
                    new(2025, 1, 1),
                    new(2025, 12, 31),
                    29_907_000_000m,
                    FullYear
                ),
                Fact(
                    hsbc,
                    assets,
                    2025,
                    SecFiscalPeriod.FullYear,
                    new(2025, 12, 31),
                    new(2025, 12, 31),
                    3_233_034_000_000m,
                    FullYear
                ),
                Fact(
                    hsbc,
                    profit,
                    2026,
                    SecFiscalPeriod.Q2,
                    new(2026, 1, 1),
                    new(2026, 6, 30),
                    15_321_000_000m,
                    HalfYear
                ),
                Fact(
                    hsbc,
                    pretax,
                    2026,
                    SecFiscalPeriod.Q2,
                    new(2026, 1, 1),
                    new(2026, 6, 30),
                    19_522_000_000m,
                    HalfYear
                ),
                Fact(
                    hsbc,
                    assets,
                    2026,
                    SecFiscalPeriod.Q2,
                    new(2026, 6, 30),
                    new(2026, 6, 30),
                    3_438_161_000_000m,
                    HalfYear
                )
            );
        await DbContext.SaveChangesAsync();
    }

    private static readonly (DocumentType Form, DateOnly Filed, string Accession) FullYear = (
        DocumentType.TwentyF,
        new DateOnly(2026, 2, 26),
        "0001089113-26-000010"
    );

    private static readonly (DocumentType Form, DateOnly Filed, string Accession) HalfYear = (
        DocumentType.SixK,
        new DateOnly(2026, 8, 4),
        "0001089113-26-000025"
    );

    private static FinancialConcept Ifrs(string tag) =>
        new()
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.IfrsFull,
            Tag = tag,
            Label = tag,
        };

    private static FinancialFact Fact(
        EquityIssuer issuer,
        FinancialConcept concept,
        int fiscalYear,
        SecFiscalPeriod period,
        DateOnly start,
        DateOnly end,
        decimal value,
        (DocumentType Form, DateOnly Filed, string Accession) filing,
        string unit = "USD"
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            FinancialConceptId = concept.Id,
            Unit = unit,
            PeriodType = start == end ? FactPeriodType.Instant : FactPeriodType.Duration,
            PeriodStart = start,
            PeriodEnd = end,
            Value = value,
            FiscalYear = fiscalYear,
            FiscalPeriod = period,
            Form = filing.Form,
            FiledDate = filing.Filed,
            AccessionNumber = filing.Accession,
        };
}
