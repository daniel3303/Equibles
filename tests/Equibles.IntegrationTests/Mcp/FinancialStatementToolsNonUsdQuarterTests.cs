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

// A venue issuer reporting cumulative euro interims (a three-month Q1, then a six-month half year)
// gets its second quarter by subtraction, as a USD filer does.
[Collection(ParadeDbCollection.Name)]
public class FinancialStatementToolsNonUsdQuarterTests : ParadeDbMcpTestBase
{
    public FinancialStatementToolsNonUsdQuarterTests(ParadeDbFixture fixture)
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
    public async Task DefaultIncomeStatement_EuroCumulativeInterims_DerivesTheSecondQuarter()
    {
        await SeedIssuer();

        var result = await Sut().GetFinancialStatement("XPAR:PINT", statement: "income");

        result.Should().Contain("— FY2026 Q2:");
        result
            .Should()
            .Contain(
                "| Net Income | 600,000,000 | EUR | Derived quarter | 2026-04-01 | 2026-06-30 |"
            );
        result.Should().NotContain("is newer but has no").And.NotContain("$");
    }

    [Fact]
    public async Task ExplicitFirstQuarter_EuroInterim_StaysReported()
    {
        await SeedIssuer();

        var result = await Sut()
            .GetFinancialStatement("XPAR:PINT", statement: "income", year: 2026, period: "Q1");

        result
            .Should()
            .Contain("| Net Income | 400,000,000 | EUR | Reported | 2026-01-01 | 2026-03-31 |");
    }

    private async Task SeedIssuer()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "PINT",
            Name: "Paris Interim SA",
            MarketCountryCode: "FR",
            MarketIdentifierCode: "XPAR",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m,
            Isin: "FR0000000001"
        );
        issuer.Presentation.Listing.IdentitySourceUrl = "https://example.test/euronext";
        var profit = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.IfrsFull,
            Tag = "ProfitLoss",
            Label = "ProfitLoss",
        };
        DbContext.Set<EquityIssuer>().Add(issuer);
        DbContext.Set<FinancialConcept>().Add(profit);
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                Fact(issuer, profit, SecFiscalPeriod.Q1, new(2026, 3, 31), 400_000_000m, "q1"),
                Fact(issuer, profit, SecFiscalPeriod.Q2, new(2026, 6, 30), 1_000_000_000m, "h1")
            );
        await DbContext.SaveChangesAsync();
    }

    private static FinancialFact Fact(
        EquityIssuer issuer,
        FinancialConcept concept,
        SecFiscalPeriod period,
        DateOnly end,
        decimal value,
        string accession
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            FinancialConceptId = concept.Id,
            Unit = "EUR",
            PeriodType = FactPeriodType.Duration,
            PeriodStart = new DateOnly(2026, 1, 1),
            PeriodEnd = end,
            Value = value,
            FiscalYear = 2026,
            FiscalPeriod = period,
            Form = DocumentType.EsefReport,
            FiledDate = end.AddDays(30),
            AccessionNumber = accession,
        };
}
