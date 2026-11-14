using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Statements;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Depreciation and amortization reads one total on the fact tools and the cash flow statement.
/// Live repros, in millions: McDonald's FY2025 10-K tags 2,199 under DepreciationAndAmortization
/// and a 457 slice under DepreciationDepletionAndAmortization, and the alias answered 457;
/// Microsoft's FY2026 10-K tags no total, only 34,300 of depreciation and 4,700 of amortization
/// of intangible assets, and the alias answered nothing.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsToolsDepreciationTotalTests : ParadeDbMcpTestBase
{
    public FinancialFactsToolsDepreciationTotalTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools FactsSut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private FinancialStatementTools StatementSut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

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

    private void AddYear(
        EquityIssuer stock,
        FinancialConcept concept,
        int fiscalYear,
        DateOnly start,
        DateOnly end,
        decimal millions,
        string accession
    ) =>
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = concept.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = start,
                    PeriodEnd = end,
                    Value = millions * 1_000_000m,
                    FiscalYear = fiscalYear,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = end.AddDays(55),
                    AccessionNumber = accession,
                }
            );

    private async Task Seed()
    {
        var depletionTotal = Concept("DepreciationDepletionAndAmortization");
        var plainTotal = Concept("DepreciationAndAmortization");
        var depreciation = Concept("Depreciation");
        var amortization = Concept("AmortizationOfIntangibleAssets");
        var operatingCashFlow = Concept("NetCashProvidedByUsedInOperatingActivities");

        var mcdonalds = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "MCD",
            Name: "McDonald's Corporation",
            Cik: "0000063908"
        );
        DbContext.Set<EquityIssuer>().Add(mcdonalds);
        var calendarStart = new DateOnly(2025, 1, 1);
        var calendarEnd = new DateOnly(2025, 12, 31);
        const string mcdonaldsAnnual = "0000063908-26-000035";
        AddYear(mcdonalds, depletionTotal, 2025, calendarStart, calendarEnd, 457m, mcdonaldsAnnual);
        AddYear(mcdonalds, plainTotal, 2025, calendarStart, calendarEnd, 2_199m, mcdonaldsAnnual);
        AddYear(
            mcdonalds,
            operatingCashFlow,
            2025,
            calendarStart,
            calendarEnd,
            10_552m,
            mcdonaldsAnnual
        );

        var microsoft = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "MSFT",
            Name: "Microsoft Corporation",
            Cik: "0000789019"
        );
        DbContext.Set<EquityIssuer>().Add(microsoft);
        var juneStart = new DateOnly(2025, 7, 1);
        var juneEnd = new DateOnly(2026, 6, 30);
        const string microsoftAnnual = "0001193125-26-323660";
        AddYear(microsoft, depreciation, 2026, juneStart, juneEnd, 34_300m, microsoftAnnual);
        AddYear(microsoft, amortization, 2026, juneStart, juneEnd, 4_700m, microsoftAnnual);
        AddYear(microsoft, operatingCashFlow, 2026, juneStart, juneEnd, 160_000m, microsoftAnnual);
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFinancialFact_TotalBesideASlice_ReturnsTheTotal()
    {
        await Seed();

        var result = await FactsSut().GetFinancialFact("MCD", "depreciation-and-amortization");

        result.Should().Contain("| 2025-01-01 | 2025-12-31 | 2025 | FY | $2,199,000,000 | USD |");
        result.Should().NotContain("$457,000,000");
        result.Should().NotContain(DepreciationTotal.SumNote);
    }

    [Fact]
    public async Task GetFinancialFact_NoTotalTagged_ReturnsTheSumOfThePartsAndSaysSo()
    {
        await Seed();

        var result = await FactsSut().GetFinancialFact("MSFT", "d&a");

        result.Should().Contain("| 2025-07-01 | 2026-06-30 | 2026 | FY | $39,000,000,000 | USD |");
        result.Should().Contain(DepreciationTotal.SumNote);
    }

    [Fact]
    public async Task CompareFinancialFact_TotalBesideASlice_ComparesTheTotal()
    {
        await Seed();

        var result = await FactsSut()
            .CompareFinancialFact(
                ["MCD"],
                "depreciation-and-amortization",
                fiscalYear: 2025,
                fiscalPeriod: "FY"
            );

        result.Should().Contain("$2,199,000,000");
        result.Should().NotContain("$457,000,000");
    }

    [Fact]
    public async Task CompareFinancialFact_NoTotalTagged_ComparesTheSumAndSaysSo()
    {
        await Seed();

        var result = await FactsSut()
            .CompareFinancialFact(
                ["MSFT"],
                "depreciation-and-amortization",
                fiscalYear: 2026,
                fiscalPeriod: "FY"
            );

        result.Should().Contain("$39,000,000,000");
        result.Should().Contain(DepreciationTotal.SumNote);
    }

    [Fact]
    public async Task GetFinancialStatement_TotalBesideASlice_CashFlowLineIsTheTotal()
    {
        await Seed();

        var result = await StatementSut()
            .GetFinancialStatement("MCD", statement: "cashflow", year: 2025, period: "FY");

        result
            .Should()
            .Contain("| Depreciation & Amortization | $2,199,000,000 | USD | Reported |");
    }

    [Fact]
    public async Task GetFinancialStatement_NoTotalTagged_CashFlowLineIsTheSumOfTheParts()
    {
        await Seed();

        var result = await StatementSut()
            .GetFinancialStatement("MSFT", statement: "cashflow", year: 2026, period: "FY");

        result
            .Should()
            .Contain(
                "| Depreciation & Amortization | $39,000,000,000 | USD | Sum of reported parts |"
            );
        result.Should().Contain(DepreciationTotal.SumNote);
    }
}
