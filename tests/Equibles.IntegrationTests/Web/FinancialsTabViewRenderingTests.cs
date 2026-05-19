using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Xunit;

namespace Equibles.IntegrationTests.Web;

/// <summary>
/// End-to-end coverage of the Financials tab through the real Web host: routing
/// → controller → the production DI/DbContext composition (AddAllModules, which
/// only maps FinancialFact/FinancialConcept because the new csproj reference
/// makes the assembly load) → Npgsql translation of LoadFinancialsTab's queries
/// → the compiled <c>_FinancialsTab.cshtml</c> Razor partial. The direct
/// controller tests use InMemory + a hand-built module set and so cover none of
/// those seams; a value-converter/translation or composition regression would be
/// invisible to them but fails here.
/// </summary>
[Collection(WebHostCollection.Name)]
public class FinancialsTabViewRenderingTests
{
    private readonly WebHostFixture _fixture;

    public FinancialsTabViewRenderingTests(WebHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetFinancials_WithSeededFacts_RendersIncomeStatementRow()
    {
        var stockId = Guid.NewGuid();
        var conceptId = Guid.NewGuid();
        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new FinancialConcept
                {
                    Id = conceptId,
                    Taxonomy = FactTaxonomy.UsGaap,
                    Tag = "Revenues",
                    Label = "Revenues",
                }
            );
            db.Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stockId,
                    FinancialConceptId = conceptId,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2023, 1, 1),
                    PeriodEnd = new DateOnly(2023, 12, 31),
                    Value = 400_000_000_000m,
                    FiscalYear = 2023,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(2024, 6, 1),
                    AccessionNumber = "0000320193-24-000099",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/Stocks/AAPL/Financials");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Financials", "the Financials tab is rendered and active");
        html.Should().Contain("Revenue", "the seeded us-gaap:Revenues line renders");
        html.Should().Contain("FY2023", "the seeded fiscal period appears in the selector");
        html.Should().Contain("400", "the seeded revenue value is rendered in the statement table");
    }
}
