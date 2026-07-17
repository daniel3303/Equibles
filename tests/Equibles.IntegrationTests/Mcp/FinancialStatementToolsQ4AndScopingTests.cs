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
/// The Q4-under-FY trap: SEC Company Facts embeds fourth-quarter flow facts in
/// the full-year duration, so income/cash-flow period='Q4' used to validate
/// off balance-sheet instants stamped (year, Q4) and then render a table of 30
/// dashes with "no line items were reported" — reading as if the company
/// reported nothing in Q4. Period availability must be scoped to the requested
/// statement's concepts, and a failed Q4 flow request must explain the filing
/// convention instead.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialStatementToolsQ4AndScopingTests : ParadeDbMcpTestBase
{
    public FinancialStatementToolsQ4AndScopingTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialStatementTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        );

    private CommonStock AddApple()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
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
        SecFiscalPeriod period,
        FactPeriodType periodType,
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
                    Unit = "USD",
                    PeriodType = periodType,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    Value = value,
                    FiscalYear = fy,
                    FiscalPeriod = period,
                    Form = DocumentType.TenK,
                    FiledDate = filed,
                    AccessionNumber = accession,
                }
            );
    }

    [Fact]
    public async Task GetFinancialStatement_Q4IncomeWithOnlyBalanceInstantsUnderQ4_ExplainsTheFilingConventionInsteadOfEmptyTable()
    {
        var stock = AddApple();
        var revenue = AddConcept("Revenues");
        var assets = AddConcept("Assets");
        // The annual income figure — Q4 flows live inside this duration.
        AddFact(
            stock,
            revenue,
            2023,
            SecFiscalPeriod.FullYear,
            FactPeriodType.Duration,
            new DateOnly(2022, 9, 25),
            new DateOnly(2023, 9, 30),
            383_000_000_000m,
            new DateOnly(2023, 11, 3),
            "aapl-fy23"
        );
        // A balance-sheet instant stamped (2023, Q4) — the row that used to
        // validate an income-statement Q4 request.
        AddFact(
            stock,
            assets,
            2023,
            SecFiscalPeriod.Q4,
            FactPeriodType.Instant,
            new DateOnly(2023, 9, 30),
            new DateOnly(2023, 9, 30),
            352_583_000_000m,
            new DateOnly(2023, 11, 3),
            "aapl-fy23"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetFinancialStatement("AAPL", statement: "income", year: 2023, period: "Q4");

        result.Should().Contain("has no income statement data for 2023 Q4");
        result.Should().Contain("embedded in the full-year figure");
        result.Should().Contain("Use period 'FY'");
        result.Should().NotContain("| Revenue |", "no table is rendered for an unavailable period");
        result
            .Should()
            .NotContain(
                "No line items of this statement were reported",
                "the misleading empty-table footnote is gone"
            );
    }

    [Fact]
    public async Task GetFinancialStatement_StatementWithNoIngestedLines_SaysSoInsteadOfClaimingNothingIngested()
    {
        var stock = AddApple();
        var assets = AddConcept("Assets");
        AddFact(
            stock,
            assets,
            2023,
            SecFiscalPeriod.FullYear,
            FactPeriodType.Instant,
            new DateOnly(2023, 9, 30),
            new DateOnly(2023, 9, 30),
            352_583_000_000m,
            new DateOnly(2023, 11, 3),
            "aapl-fy23"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialStatement("AAPL", statement: "cashflow");

        result.Should().Contain("No cash flow line items have been ingested for AAPL");
        result
            .Should()
            .NotContain(
                "No structured financial facts have been ingested",
                "balance-sheet facts ARE ingested — only this statement is empty"
            );
    }

    [Fact]
    public async Task GetFinancialStatement_MixedFilingVintages_FlagsTheRestatementSpan()
    {
        var stock = AddApple();
        var revenue = AddConcept("Revenues");
        var netIncome = AddConcept("NetIncomeLoss");
        AddFact(
            stock,
            revenue,
            2023,
            SecFiscalPeriod.FullYear,
            FactPeriodType.Duration,
            new DateOnly(2022, 9, 25),
            new DateOnly(2023, 9, 30),
            383_000_000_000m,
            new DateOnly(2023, 11, 3),
            "aapl-fy23"
        );
        // Net income restated by a later filing — the statement now mixes
        // filing vintages and must say so.
        AddFact(
            stock,
            netIncome,
            2023,
            SecFiscalPeriod.FullYear,
            FactPeriodType.Duration,
            new DateOnly(2022, 9, 25),
            new DateOnly(2023, 9, 30),
            97_000_000_000m,
            new DateOnly(2025, 10, 31),
            "aapl-fy25-restate"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialStatement("AAPL", statement: "income", year: 2023);

        result.Should().Contain("source filings span 2023-11-03 to 2025-10-31");
    }
}
