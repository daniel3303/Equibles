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
/// Two EBS defects the tools passed on. Its 10-K quarterly tables tagged operating income as
/// GrossProfit (FY2017 Q3: 47.8M beside OperatingIncomeLoss 47,769,000, while revenue 149.4M less
/// cost 44,503,000 is 104,897,000). And it tagged its cost total OperatingExpenses through FY2019,
/// then OperatingCostsAndExpenses from FY2018, the same figures in FY2018 and FY2019, so
/// "operating-expenses" stopped at FY2019 while FY2025's 642.8M sat on another line.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialToolsMistaggedGrossProfitAndOperatingCostsTests : ParadeDbMcpTestBase
{
    public FinancialToolsMistaggedGrossProfitAndOperatingCostsTests(ParadeDbFixture fixture)
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

    private readonly Dictionary<string, FinancialConcept> _concepts = [];

    private FinancialConcept Concept(string tag)
    {
        if (_concepts.TryGetValue(tag, out var existing))
            return existing;
        var concept = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = tag,
            Label = tag,
        };
        DbContext.Set<FinancialConcept>().Add(concept);
        _concepts[tag] = concept;
        return concept;
    }

    private void Add(
        EquityIssuer stock,
        string tag,
        int fiscalYear,
        SecFiscalPeriod period,
        DateOnly start,
        DateOnly end,
        decimal value
    )
    {
        var annual = period == SecFiscalPeriod.FullYear;
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = Concept(tag).Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = start,
                    PeriodEnd = end,
                    Value = value,
                    FiscalYear = fiscalYear,
                    FiscalPeriod = period,
                    Form = annual ? DocumentType.TenK : DocumentType.TenQ,
                    FiledDate = end.AddDays(annual ? 55 : 35),
                    AccessionNumber = $"0001367644-{end:yy}-{end.DayOfYear:000000}",
                    DimensionsKey = "",
                }
            );
    }

    private void AddYear(EquityIssuer stock, string tag, int year, decimal millions) =>
        Add(
            stock,
            tag,
            year,
            SecFiscalPeriod.FullYear,
            new DateOnly(year, 1, 1),
            new DateOnly(year, 12, 31),
            millions * 1_000_000m
        );

    private void AddQ3(EquityIssuer stock, string tag, decimal value) =>
        Add(
            stock,
            tag,
            2017,
            SecFiscalPeriod.Q3,
            new DateOnly(2017, 7, 1),
            new DateOnly(2017, 9, 30),
            value
        );

    private async Task SeedEbs()
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "EBS",
            Name: "Emergent BioSolutions Inc.",
            Cik: "0001367644"
        );
        DbContext.Set<EquityIssuer>().Add(stock);
        AddQ3(stock, "SalesRevenueNet", 149_400_000m);
        AddQ3(stock, "CostOfGoodsAndServicesSold", 44_503_000m);
        AddQ3(stock, "GrossProfit", 47_800_000m);
        AddQ3(stock, "OperatingIncomeLoss", 47_769_000m);
        AddYear(stock, "OperatingExpenses", 2017, 436.6m);
        AddYear(stock, "OperatingExpenses", 2018, 692.6m);
        AddYear(stock, "OperatingExpenses", 2019, 991.9m);
        AddYear(stock, "OperatingCostsAndExpenses", 2018, 692.6m);
        AddYear(stock, "OperatingCostsAndExpenses", 2019, 991.9m);
        AddYear(stock, "OperatingCostsAndExpenses", 2025, 642.8m);
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFinancialStatement_GrossProfitIsTheOperatingIncome_ShowsRevenueLessCost()
    {
        await SeedEbs();

        var result = await StatementSut()
            .GetFinancialStatement("EBS", statement: "income", year: 2017, period: "Q3");

        result.Should().Contain("| Gross Profit | $104,897,000 | USD | Derived |");
        result.Should().Contain("| Operating Income | $47,769,000 | USD | Reported |");
        result.Should().Contain("_Gross Profit is Revenue − Cost of Revenue for a period");
        result.Should().NotContain("$47,800,000");
    }

    [Fact]
    public async Task GetFinancialStatement_ClaimedOperatingCosts_ReadAsTotalOperatingExpenses()
    {
        await SeedEbs();

        var result = await StatementSut()
            .GetFinancialStatement("EBS", statement: "income", year: 2025, period: "FY");

        result.Should().Contain("| Total Operating Expenses | $642,800,000 | USD | Reported |");
        result.Should().NotContain("| Operating Costs & Expenses |");
    }

    [Fact]
    public async Task GetFinancialFact_GrossProfitIsTheOperatingIncome_KeepsTheFigureAndWarns()
    {
        await SeedEbs();

        var result = await FactsSut().GetFinancialFact("EBS", "gross-profit");

        result
            .Should()
            .Contain("| 2017-07-01 | 2017-09-30 | 2017 | Q3 | $47,800,000 | USD | 10-Q |");
        result
            .Should()
            .Contain("_Tag warning: for FY2017 Q3 the figure tagged as gross profit equals");
        result.Should().Contain("GetFinancialStatement shows Revenue − Cost of Revenue");
    }

    [Fact]
    public async Task GetFinancialFact_OperatingExpenses_ContinueThroughClaimedOperatingCosts()
    {
        await SeedEbs();

        var result = await FactsSut().GetFinancialFact("EBS", "operating-expenses");

        result
            .Should()
            .Contain("| 2025-01-01 | 2025-12-31 | 2025 | FY | $642,800,000 | USD | 10-K |");
        result
            .Should()
            .Contain("| 2017-01-01 | 2017-12-31 | 2017 | FY | $436,600,000 | USD | 10-K |");
    }

    [Fact]
    public async Task CompareFinancialFact_OperatingExpenses_ReadTheClaimedOperatingCosts()
    {
        await SeedEbs();

        var result = await FactsSut()
            .CompareFinancialFact(["EBS"], "operating-expenses", fiscalYear: 2025);

        result.Should().Contain("$642,800,000");
    }

    [Fact]
    public async Task CompareFinancialFact_GrossProfitIsTheOperatingIncome_Warns()
    {
        await SeedEbs();

        var result = await FactsSut()
            .CompareFinancialFact(["EBS"], "gross-profit", fiscalYear: 2017, fiscalPeriod: "Q3");

        result.Should().Contain("$47,800,000");
        result.Should().Contain("_Tag warning: the figure EBS tagged as gross profit");
    }
}
