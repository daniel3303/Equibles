using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class FinancialFactRepositoryCostOfSalesTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly Start2025 = new(2025, 1, 1);
    private static readonly DateOnly End2025 = new(2025, 12, 31);

    [Fact]
    public async Task Consolidated_KeepsIfrsCostOfSales_OnlyWhenTheSameFilingPresentsExpensesByFunction()
    {
        var issuer = new EquityIssuer { Name = "Expense presentation issuer" };
        var costOfSales = Concept(FactTaxonomy.IfrsFull, "CostOfSales");
        var expenseByNature = Concept(FactTaxonomy.IfrsFull, "ExpenseByNature");
        var administrative = Concept(FactTaxonomy.IfrsFull, "AdministrativeExpense");
        var usCostOfRevenue = Concept(FactTaxonomy.UsGaap, "CostOfRevenue");
        DbContext.AddRange(issuer, costOfSales, expenseByNature, administrative, usCostOfRevenue);
        await DbContext.SaveChangesAsync();

        FinancialFact Fact(
            FinancialConcept concept,
            string accession,
            decimal value,
            DateOnly? start = null,
            string dimension = ""
        ) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                FinancialConceptId = concept.Id,
                Unit = "EUR",
                Value = value,
                PeriodType = FactPeriodType.Duration,
                PeriodStart = start ?? Start2025,
                PeriodEnd = End2025,
                FiscalYear = 2025,
                FiscalPeriod = SecFiscalPeriod.FullYear,
                Form = DocumentType.EsefAnnualReport,
                FiledDate = new(2026, 3, 18),
                AccessionNumber = accession,
                DimensionsKey = dimension,
            };

        DbContext.AddRange(
            // CTT's shape: a total of expenses by nature, and cost of sales covering goods sold only.
            Fact(costOfSales, "by-nature", 10_434_432m),
            Fact(expenseByNature, "by-nature", 1_200_252_755m),
            // A function-of-expense statement: cost of sales is the whole cost of revenue.
            Fact(costOfSales, "by-function", 700m),
            Fact(administrative, "by-function", 100m),
            // Function evidence for another period or a segment proves nothing about this line.
            Fact(costOfSales, "other-period", 300m),
            Fact(administrative, "other-period", 50m, start: new(2025, 7, 1)),
            Fact(administrative, "other-period", 50m, dimension: "segment"),
            // US-GAAP cost of revenue is never gated.
            Fact(usCostOfRevenue, "us-gaap", 900m)
        );
        await DbContext.SaveChangesAsync();

        var repository = new FinancialFactRepository(DbContext);
        var single = await repository
            .GetConsolidatedByIssuerId(issuer.Id)
            .Select(f => new { f.AccessionNumber, f.FinancialConcept.Tag })
            .ToListAsync();
        var batch = await repository
            .GetConsolidatedByIssuerIds([issuer.Id])
            .Select(f => new { f.AccessionNumber, f.FinancialConcept.Tag })
            .ToListAsync();

        var expected = new[]
        {
            new { AccessionNumber = "by-nature", Tag = "ExpenseByNature" },
            new { AccessionNumber = "by-function", Tag = "CostOfSales" },
            new { AccessionNumber = "by-function", Tag = "AdministrativeExpense" },
            new { AccessionNumber = "other-period", Tag = "AdministrativeExpense" },
            new { AccessionNumber = "us-gaap", Tag = "CostOfRevenue" },
        };
        single.Should().BeEquivalentTo(expected);
        batch.Should().BeEquivalentTo(expected);
        // The raw fact itself stays stored; only the consolidated statement read drops it.
        (
            await repository
                .GetByIssuerId(issuer.Id)
                .CountAsync(f => f.FinancialConceptId == costOfSales.Id)
        )
            .Should()
            .Be(3);
    }

    private static FinancialConcept Concept(FactTaxonomy taxonomy, string tag) =>
        new()
        {
            Taxonomy = taxonomy,
            Tag = tag,
            Label = tag,
        };
}
