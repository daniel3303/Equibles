using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// An IFRS filer that presents expenses by nature tags CostOfSales for goods sold only, so it is
/// not the cost of revenue; the statement tool must drop it the way every consolidated read does.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialStatementToolsByNatureCostOfSalesTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task GetFinancialStatement_IfrsCostOfSales_ShowsOnlyBesideAFunctionOfExpenseLine(
        bool filingStatesGrossProfit,
        bool costOfRevenueShown
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "CTT",
            Name: "Expense by nature issuer",
            Cik: "0001999999"
        );
        var revenue = Concept("Revenue");
        var costOfSales = Concept("CostOfSales");
        var grossProfit = Concept("GrossProfit");
        DbContext.Set<EquityIssuer>().Add(stock);
        DbContext.Set<FinancialConcept>().AddRange(revenue, costOfSales, grossProfit);
        DbContext
            .Set<FinancialFact>()
            .AddRange(Fact(stock, revenue, 1_100_000_000m), Fact(stock, costOfSales, 10_434_432m));
        if (filingStatesGrossProfit)
            DbContext.Set<FinancialFact>().Add(Fact(stock, grossProfit, 1_089_565_568m));
        await DbContext.SaveChangesAsync();

        var result = await new FinancialStatementTools(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>()
        ).GetFinancialStatement("CTT", statement: "income", year: 2025);

        result.Should().Contain("1,100,000,000");
        if (costOfRevenueShown)
            result.Should().Contain("10,434,432");
        else
            result.Should().NotContain("10,434,432");
    }

    private static FinancialConcept Concept(string tag) =>
        new()
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.IfrsFull,
            Tag = tag,
            Label = tag,
        };

    private static FinancialFact Fact(
        EquityIssuer stock,
        FinancialConcept concept,
        decimal value
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = stock.Id,
            FinancialConceptId = concept.Id,
            Unit = "EUR",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2025, 1, 1),
            PeriodEnd = new DateOnly(2025, 12, 31),
            Value = value,
            FiscalYear = 2025,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = DocumentType.TwentyF,
            FiledDate = new DateOnly(2026, 3, 18),
            AccessionNumber = "0001999999-26-000001",
        };
}
