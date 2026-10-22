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
/// A bank's revenue reads net interest plus noninterest income when its Revenues tag is a fee
/// slice. Live repro: Zions' FY2026 10-Qs tag Revenues 172M (Q1) and 182M (Q2) beside 662M + 187M
/// and 677M + 460M, so GetFinancialFact revenue returned 182M for a bank with a 1.14B top line.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsToolsBankRevenueTests : ParadeDbMcpTestBase
{
    public FinancialFactsToolsBankRevenueTests(ParadeDbFixture fixture)
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

    private void AddQuarter(
        EquityIssuer stock,
        FinancialConcept concept,
        SecFiscalPeriod period,
        decimal millions
    )
    {
        var q1 = period == SecFiscalPeriod.Q1;
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
                    PeriodStart = q1 ? new DateOnly(2026, 1, 1) : new DateOnly(2026, 4, 1),
                    PeriodEnd = q1 ? new DateOnly(2026, 3, 31) : new DateOnly(2026, 6, 30),
                    Value = millions * 1_000_000m,
                    FiscalYear = 2026,
                    FiscalPeriod = period,
                    Form = DocumentType.TenQ,
                    FiledDate = q1 ? new DateOnly(2026, 5, 7) : new DateOnly(2026, 8, 6),
                    AccessionNumber = q1 ? "0000109380-26-000083" : "0000109380-26-000111",
                }
            );
    }

    private async Task SeedZions(bool tagsRevenues = true)
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "ZION",
            Name: "Zions Bancorporation, National Association",
            Cik: "0000109380"
        );
        DbContext.Set<EquityIssuer>().Add(stock);
        var revenues = Concept("Revenues");
        var netInterest = Concept("InterestIncomeExpenseNet");
        var noninterest = Concept("NoninterestIncome");
        if (tagsRevenues)
        {
            AddQuarter(stock, revenues, SecFiscalPeriod.Q1, 172m);
            AddQuarter(stock, revenues, SecFiscalPeriod.Q2, 182m);
        }
        AddQuarter(stock, netInterest, SecFiscalPeriod.Q1, 662m);
        AddQuarter(stock, netInterest, SecFiscalPeriod.Q2, 677m);
        AddQuarter(stock, noninterest, SecFiscalPeriod.Q1, 187m);
        AddQuarter(stock, noninterest, SecFiscalPeriod.Q2, 460m);
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFinancialFact_RevenuesIsAFeeSlice_ReturnsTheBankTopLine()
    {
        await SeedZions();

        var result = await FactsSut().GetFinancialFact("ZION", "revenue");

        result
            .Should()
            .Contain("| 2026-04-01 | 2026-06-30 | 2026 | Q2 | $1,137,000,000 | USD | 10-Q |");
        result
            .Should()
            .Contain("| 2026-01-01 | 2026-03-31 | 2026 | Q1 | $849,000,000 | USD | 10-Q |");
        result.Should().NotContain("$182,000,000");
    }

    [Fact]
    public async Task GetFinancialFact_BankWithoutARevenueTag_ReturnsTheBankTopLine()
    {
        await SeedZions(tagsRevenues: false);

        var result = await FactsSut().GetFinancialFact("ZION", "revenue");

        result.Should().Contain("$1,137,000,000");
    }

    [Fact]
    public async Task CompareFinancialFact_RevenuesIsAFeeSlice_ComparesTheBankTopLine()
    {
        await SeedZions();

        var result = await FactsSut()
            .CompareFinancialFact(["ZION"], "revenue", fiscalYear: 2026, fiscalPeriod: "Q2");

        result.Should().Contain("$1,137,000,000");
        result.Should().NotContain("$182,000,000");
    }

    [Fact]
    public async Task GetFinancialStatement_RevenuesIsAFeeSlice_RevenueLineIsTheBankTopLine()
    {
        await SeedZions();

        var result = await StatementSut()
            .GetFinancialStatement("ZION", statement: "income", year: 2026, period: "Q2");

        result.Should().Contain("| Revenue | $1,137,000,000 | USD | Reported |");
        result.Should().Contain("| Net Interest Income | $677,000,000 |");
    }
}
