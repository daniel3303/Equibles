using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FinancialFactsCompareToolsTests : ParadeDbMcpTestBase
{
    public FinancialFactsCompareToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private CommonStock AddStock(string ticker, string name)
    {
        var s = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = name,
            Cik = ticker,
        };
        DbContext.Set<CommonStock>().Add(s);
        return s;
    }

    private FinancialConcept _revenue;

    private FinancialConcept RevenueConcept()
    {
        if (_revenue != null)
            return _revenue;
        _revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        DbContext.Set<FinancialConcept>().Add(_revenue);
        return _revenue;
    }

    private void AddRevenue(
        CommonStock stock,
        decimal value,
        DateOnly filed,
        int fy = 2023,
        string accn = null
    )
    {
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = stock.Id,
                    FinancialConceptId = RevenueConcept().Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(fy, 1, 1),
                    PeriodEnd = new DateOnly(fy, 12, 31),
                    Value = value,
                    FiscalYear = fy,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = filed,
                    AccessionNumber = accn ?? Guid.NewGuid().ToString(),
                }
            );
    }

    [Fact]
    public async Task CompareFinancialFact_NoTickers_ReturnsRequired()
    {
        var result = await Sut().CompareFinancialFact("", "revenue", 2023);

        result.Should().Contain("At least one ticker is required.");
    }

    [Fact]
    public async Task CompareFinancialFact_UnknownConcept_ListsSupportedAliases()
    {
        var result = await Sut().CompareFinancialFact("AAPL", "ebitda", 2023);

        result.Should().Contain("Unknown concept 'ebitda'");
    }

    [Fact]
    public async Task CompareFinancialFact_TooManyTickers_ReturnsCapMessage()
    {
        var many = string.Join(",", Enumerable.Range(1, 30).Select(i => $"T{i}"));

        var result = await Sut().CompareFinancialFact(many, "revenue", 2023);

        result.Should().Contain("Too many tickers (30). The maximum is 25.");
    }

    [Fact]
    public async Task CompareFinancialFact_PeerSet_RendersRowsLatestRestatedAndListsSkipped()
    {
        var apple = AddStock("AAPL", "Apple Inc.");
        var msft = AddStock("MSFT", "Microsoft Corp");
        AddStock("GOOGL", "Alphabet Inc."); // no facts
        // Apple restated: 380 then 400 (latest filed wins).
        AddRevenue(apple, 380_000_000_000m, new DateOnly(2024, 1, 15), accn: "aapl-orig");
        AddRevenue(apple, 400_000_000_000m, new DateOnly(2024, 6, 1), accn: "aapl-restate");
        AddRevenue(msft, 211_000_000_000m, new DateOnly(2024, 1, 20), accn: "msft");
        await DbContext.SaveChangesAsync();

        var result = await Sut().CompareFinancialFact("aapl, msft, googl, zzzz", "revenue", 2023);

        result.Should().Contain("revenue — 2023 FY peer comparison:");
        result.Should().Contain("| AAPL | Apple Inc. | $400,000,000,000 | USD |");
        result.Should().Contain("| MSFT | Microsoft Corp | $211,000,000,000 | USD |");
        result.Should().NotContain("$380,000,000,000", "the latest-filed restatement wins");
        result.Should().Contain("Skipped:");
        result.Should().Contain("GOOGL (no data)");
        result.Should().Contain("ZZZZ (not found)");
    }

    [Fact]
    public async Task CompareFinancialFact_ConceptNeverIngested_ReturnsNotIngested()
    {
        AddStock("AAPL", "Apple Inc.");
        await DbContext.SaveChangesAsync();

        var result = await Sut().CompareFinancialFact("AAPL", "revenue", 2023);

        result.Should().Contain("No 'revenue' data has been ingested.");
        result.Should().NotContain("(no data)", "an un-ingested concept is not a per-ticker miss");
    }

    [Fact]
    public async Task CompareFinancialFact_SameDayAmendments_DeterministicByAccession()
    {
        var apple = AddStock("AAPL", "Apple Inc.");
        var filed = new DateOnly(2024, 6, 1);
        // Two filings, SAME filed date — accession is the stable tiebreak.
        AddRevenue(apple, 350_000_000_000m, filed, accn: "0000320193-24-000001");
        AddRevenue(apple, 360_000_000_000m, filed, accn: "0000320193-24-000099");
        await DbContext.SaveChangesAsync();

        var first = await Sut().CompareFinancialFact("AAPL", "revenue", 2023);
        var second = await Sut().CompareFinancialFact("AAPL", "revenue", 2023);

        first.Should().Contain("$360,000,000,000", "the higher accession wins the same-day tie");
        first.Should().NotContain("$350,000,000,000");
        second.Should().Be(first, "the pick is deterministic across calls");
    }
}
