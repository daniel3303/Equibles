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
/// Diluted EPS reads a year the filer tags only as its continuing operations' on the fact tools.
/// Live repro: Freeport-McMoRan's 2025 10-Qs tag <c>EarningsPerShareDiluted</c> (0.24, 0.53,
/// 0.46) and its 10-K the year only as <c>IncomeLossFromContinuingOperationsPerDilutedShare</c>
/// (1.52) beside the figures that prove it the whole, and the alias answered no year. The second
/// issuer is made up: its 10-K also states discontinued operations, so its continuing figure is no
/// diluted EPS.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FinancialFactsToolsContinuingOperationsEpsTests : ParadeDbMcpTestBase
{
    public FinancialFactsToolsContinuingOperationsEpsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools Sut() =>
        new(
            new StatementFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private FinancialStatementTools StatementSut() =>
        new(
            new StatementFactRepository(DbContext),
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

    private void Add(
        EquityIssuer stock,
        FinancialConcept concept,
        string start,
        string end,
        decimal value,
        SecFiscalPeriod period,
        string accession,
        string filed,
        string unit = "USD/shares"
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
                    FinancialConceptId = concept.Id,
                    Unit = unit,
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = DateOnly.Parse(start),
                    PeriodEnd = DateOnly.Parse(end),
                    Value = value,
                    FiscalYear = 2025,
                    FiscalPeriod = period,
                    Form = annual ? DocumentType.TenK : DocumentType.TenQ,
                    FiledDate = DateOnly.Parse(filed),
                    AccessionNumber = accession,
                }
            );
    }

    private async Task Seed()
    {
        var diluted = Concept("EarningsPerShareDiluted");
        var continuing = Concept("IncomeLossFromContinuingOperationsPerDilutedShare");
        var discontinued = Concept("IncomeLossFromDiscontinuedOperationsNetOfTax");
        // The 10-K's figures that prove the year's continuing EPS the whole (ContinuingOperationsEps).
        var proof = new[]
        {
            (
                Concept(
                    "IncomeLossFromContinuingOperationsIncludingPortionAttributableToNoncontrollingInterest"
                ),
                4_152_000_000m,
                "USD"
            ),
            (Concept("ProfitLoss"), 4_152_000_000m, "USD"),
            (Concept("WeightedAverageNumberOfDilutedSharesOutstanding"), 1_443_000_000m, "shares"),
            (Concept("NetIncomeLossAvailableToCommonStockholdersDiluted"), 2_197_000_000m, "USD"),
        };

        var freeport = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "FCX",
            Name: "Freeport-McMoRan Inc.",
            Cik: "0000831259"
        );
        var recast = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "DSCO",
            Name: "Discontinued Example Corp",
            Cik: "0000000001"
        );
        DbContext.Set<EquityIssuer>().AddRange(freeport, recast);
        foreach (var (stock, prefix) in new[] { (freeport, "0000831259"), (recast, "0000000001") })
        {
            var first = $"{prefix}-25-000020";
            var second = $"{prefix}-25-000035";
            var third = $"{prefix}-25-000050";
            var annual = $"{prefix}-26-000012";
            Add(
                stock,
                diluted,
                "2025-01-01",
                "2025-03-31",
                0.24m,
                SecFiscalPeriod.Q1,
                first,
                "2025-05-08"
            );
            Add(
                stock,
                diluted,
                "2025-04-01",
                "2025-06-30",
                0.53m,
                SecFiscalPeriod.Q2,
                second,
                "2025-08-07"
            );
            Add(
                stock,
                diluted,
                "2025-07-01",
                "2025-09-30",
                0.46m,
                SecFiscalPeriod.Q3,
                third,
                "2025-11-06"
            );
            Add(
                stock,
                continuing,
                "2025-01-01",
                "2025-12-31",
                1.52m,
                SecFiscalPeriod.FullYear,
                annual,
                "2026-02-13"
            );
            foreach (var (concept, value, unit) in proof)
                Add(
                    stock,
                    concept,
                    "2025-01-01",
                    "2025-12-31",
                    value,
                    SecFiscalPeriod.FullYear,
                    annual,
                    "2026-02-13",
                    unit
                );
            if (stock == recast)
                Add(
                    stock,
                    discontinued,
                    "2025-01-01",
                    "2025-12-31",
                    -150_000_000m,
                    SecFiscalPeriod.FullYear,
                    annual,
                    "2026-02-13",
                    unit: "USD"
                );
        }
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetFinancialFact_YearTaggedOnlyAsContinuingOperations_ReturnsTheYear()
    {
        await Seed();

        var result = await Sut().GetFinancialFact("FCX", "eps-diluted");

        result.Should().Contain("| 2025-01-01 | 2025-12-31 | 2025 | FY | $1.52 |");
    }

    [Fact]
    public async Task GetFinancialFact_FilingStatesDiscontinuedOperations_ReturnsNoYear()
    {
        await Seed();

        var result = await Sut().GetFinancialFact("DSCO", "eps-diluted");

        result.Should().NotContain("1.52");
        result.Should().Contain("| 2025-07-01 | 2025-09-30 | 2025 | Q3 | $0.46 |");
    }

    [Fact]
    public async Task CompareFinancialFact_YearTaggedOnlyAsContinuingOperations_ComparesTheYear()
    {
        await Seed();

        var result = await Sut()
            .CompareFinancialFact(
                ["FCX", "DSCO"],
                "eps-diluted",
                fiscalYear: 2025,
                fiscalPeriod: "FY"
            );

        result.Should().Contain("$1.52 |");
        result.Split('\n').Count(line => line.Contains("1.52")).Should().Be(1);
    }

    [Theory]
    [InlineData("FCX", true)]
    [InlineData("DSCO", false)]
    public async Task GetFinancialStatement_YearTaggedOnlyAsContinuingOperations_LineReadsItWhereProved(
        string ticker,
        bool read
    )
    {
        await Seed();

        var result = await StatementSut()
            .GetFinancialStatement(ticker, statement: "income", year: 2025, period: "FY");

        result.Contains("| EPS (Diluted) | $1.52 | USD/shares | Reported |").Should().Be(read);
    }
}
