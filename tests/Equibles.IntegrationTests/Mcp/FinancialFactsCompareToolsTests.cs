using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Mcp;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Captures;
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
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private EquityIssuer AddStock(string ticker, string name)
    {
        EquityIssuer s = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: name,
            Cik: ticker
        );
        DbContext.Set<EquityIssuer>().Add(s);
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
        EquityIssuer stock,
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
                    EquityIssuerId = stock.Id,
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

    [Theory]
    [InlineData("ſPY")]
    [InlineData("AAPL/../../x")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567")]
    [InlineData("AAPL,,MSFT")]
    public async Task CompareFinancialFact_InvalidBatchFailsBeforeRepositoryAccess(string tickers)
    {
        var sut = new FinancialFactsTools(
            new FinancialFactRepository(null),
            new FinancialConceptRepository(null),
            new EquityIssuerRepository(null),
            new StockSplitRepository(null),
            new Equibles.Errors.BusinessLogic.ErrorManager(null),
            NullLogger<FinancialFactsTools>()
        );

        var result = await sut.CompareFinancialFact(tickers, "revenue", 2023);

        result.Should().Contain("Invalid ticker");
    }

    [Fact]
    public async Task CompareFinancialFact_PeerSet_RendersRowsLatestRestatedAndListsSkipped()
    {
        EquityIssuer apple = AddStock("AAPL", "Apple Inc.");
        EquityIssuer msft = AddStock("MSFT", "Microsoft Corp");
        AddStock("GOOGL", "Alphabet Inc."); // no facts
        // Apple restated: 380 then 400 (latest filed wins).
        AddRevenue(apple, 380_000_000_000m, new DateOnly(2024, 1, 15), accn: "aapl-orig");
        AddRevenue(apple, 400_000_000_000m, new DateOnly(2024, 6, 1), accn: "aapl-restate");
        AddRevenue(msft, 211_000_000_000m, new DateOnly(2024, 1, 20), accn: "msft");
        await DbContext.SaveChangesAsync();

        using var scope = McpResultCapture.Begin();
        var result = await Sut().CompareFinancialFact("aapl, msft, googl, zzzz", "revenue", 2023);

        var capture = scope.Get<FinancialFactComparisonCapture>();
        capture
            .Rows.Select(row => (row.Ticker, row.Symbol, row.Fact.Value))
            .Should()
            .Equal(("AAPL", "AAPL", 400_000_000_000m), ("MSFT", "MSFT", 211_000_000_000m));
        capture
            .Notes.Should()
            .Contain(note => note.StartsWith("Skipped:") && note.Contains("GOOGL (no data)"));

        result.Should().Contain("revenue — 2023 FY peer comparison:");
        result.Should().Contain("| AAPL | Apple Inc. | $400,000,000,000 | USD |");
        result.Should().Contain("| MSFT | Microsoft Corp | $211,000,000,000 | USD |");
        result.Should().NotContain("$380,000,000,000", "the latest-filed restatement wins");
        result.Should().Contain("Skipped:");
        result.Should().Contain("GOOGL (no data)");
        result.Should().Contain("ZZZZ (not found in the tracked SEC issuer set)");
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("unresolved")]
    [InlineData("foreign")]
    public async Task CompareFinancialFact_PerShareValue_RestatesEachCompanyAcrossItsSplits(
        string splitScope
    )
    {
        EquityIssuer alphabet = AddStock("GOOGL", "Alphabet Inc.");
        var dilutedEps = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "EarningsPerShareDiluted",
            Label = "Diluted EPS",
        };
        DbContext.Set<FinancialConcept>().Add(dilutedEps);
        var splitListingId = (Guid?)alphabet.Presentation.EquityListingId;
        if (splitScope == "unresolved")
            splitListingId = null;
        if (splitScope == "foreign")
        {
            var foreign = new EquityListing
            {
                EquitySecurityId = alphabet.Presentation.Listing.EquitySecurityId,
                Ticker = "GOOGL",
                MarketCountryCode = "PT",
                MarketIdentifierCode = "XLIS",
            };
            DbContext.Add(foreign);
            splitListingId = foreign.Id;
        }
        DbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    EquityIssuerId = alphabet.Id,
                    EquityListingId = splitListingId,
                    PriceSeriesTicker = splitScope == "unresolved" ? null : "GOOGL",
                    EffectiveDate = new DateOnly(2022, 7, 18),
                    Numerator = 20m,
                    Denominator = 1m,
                }
            );
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    EquityIssuerId = alphabet.Id,
                    FinancialConceptId = dilutedEps.Id,
                    Unit = "USD/shares",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2021, 1, 1),
                    PeriodEnd = new DateOnly(2021, 12, 31),
                    Value = 20m,
                    FiscalYear = 2021,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = new DateOnly(2022, 2, 2),
                    AccessionNumber = "googl-fy21",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().CompareFinancialFact("GOOGL", "eps-diluted", 2021);

        var expected =
            splitScope == "primary" ? "$1.00"
            : splitScope == "unresolved" ? "$20.00 (as filed)"
            : "$20.00";
        result.Should().Contain($"| GOOGL | Alphabet Inc. | {expected} | USD/shares |");
        if (splitScope == "primary")
            result.Should().Contain("are split-adjusted to today's share basis");
        else
            result.Should().NotContain("are split-adjusted to today's share basis");
        if (splitScope == "unresolved")
            result.Should().Contain("split attribution is unresolved");
        else
            result.Should().NotContain("split attribution is unresolved");
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
        EquityIssuer apple = AddStock("AAPL", "Apple Inc.");
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
    // ── Calendar basis ─────────────────────────────────────────────────

    private void AddFiscalYear(EquityIssuer stock, int stamp, string start, string end, decimal value) =>
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = RevenueConcept().Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = DateOnly.Parse(start),
                    PeriodEnd = DateOnly.Parse(end),
                    Value = value,
                    FiscalYear = stamp,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = DateOnly.Parse(end).AddDays(40),
                    AccessionNumber = Guid.NewGuid().ToString("N")[..20],
                }
            );

    // "Compare Nvidia, AMD and Microsoft revenue for 2025": NVDA's fiscal 2025 is mostly calendar 2024, so the
    // calendar row is its fiscal 2026; MSFT's June year end splits about evenly and says so. Periods as filed.
    [Fact]
    public async Task CompareFinancialFact_CalendarBasis_TakesEachCompanysYearFallingMostlyInTheYear()
    {
        var nvda = AddStock("NVDA", "NVIDIA Corp");
        var amd = AddStock("AMD", "Advanced Micro Devices");
        var msft = AddStock("MSFT", "Microsoft Corp");
        AddFiscalYear(nvda, 2025, "2024-01-29", "2025-01-26", 130_497_000_000m);
        AddFiscalYear(nvda, 2026, "2025-01-27", "2026-01-25", 215_938_000_000m);
        AddFiscalYear(amd, 2025, "2024-12-29", "2025-12-27", 34_639_000_000m);
        AddFiscalYear(msft, 2025, "2024-07-01", "2025-06-30", 281_724_000_000m);
        AddFiscalYear(msft, 2026, "2025-07-01", "2026-06-30", 318_000_000_000m);
        await DbContext.SaveChangesAsync();
        using var scope = McpResultCapture.Begin();

        var result = await Sut().CompareFinancialFact("NVDA,AMD,MSFT", "revenue", 2025, "FY", "calendar");

        result.Should().Contain("calendar 2025 peer comparison");
        result.Should().Contain("| FY2026 | 2025-01-27 to 2026-01-25 |");
        result.Should().Contain("| FY2025 | 2024-12-29 to 2025-12-27 |");
        result.Should().Contain("| FY2026 † | 2025-07-01 to 2026-06-30 |");
        result.Should().Contain("† This fiscal year splits about evenly");
        result.Should().NotContain("2024-01-29");
        var capture = scope.Get<FinancialFactComparisonCapture>();
        capture.Calendar.Should().BeTrue();
        capture.Rows.Select(row => row.Fact.FiscalYear).Should().Equal(2026, 2025, 2026);
    }

    [Fact]
    public async Task CompareFinancialFact_CalendarBasisYearNotYetFiled_ListsTheCompanyAsSkipped()
    {
        var nvda = AddStock("NVDA", "NVIDIA Corp");
        AddFiscalYear(nvda, 2025, "2024-01-29", "2025-01-26", 130_497_000_000m);
        await DbContext.SaveChangesAsync();

        var result = await Sut().CompareFinancialFact("NVDA", "revenue", 2025, "FY", "calendar");

        result.Should().NotContain("2025-01-26");
        result.Should().Contain("Skipped: NVDA");
    }

    [Fact]
    public async Task CompareFinancialFact_CalendarBasisForAQuarter_IsRefused()
    {
        var result = await Sut().CompareFinancialFact("NVDA,AMD", "revenue", 2025, "Q2", "calendar");

        result.Should().Contain("whole fiscal years only");
    }
}
