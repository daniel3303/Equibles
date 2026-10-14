using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FinancialFactsToolsTests : ParadeDbMcpTestBase
{
    public FinancialFactsToolsTests(ParadeDbFixture fixture)
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

    private static EquityIssuer Apple() =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );

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
        EquityIssuer stock,
        FinancialConcept concept,
        int fy,
        SecFiscalPeriod period,
        decimal value,
        DateOnly filed,
        DocumentType form,
        string accn
    )
    {
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
                    PeriodStart = new DateOnly(fy, 1, 1),
                    PeriodEnd = new DateOnly(fy, 12, 31),
                    Value = value,
                    FiscalYear = fy,
                    FiscalPeriod = period,
                    Form = form,
                    FiledDate = filed,
                    AccessionNumber = accn,
                }
            );
    }

    [Fact]
    public async Task GetFinancialFact_UnknownTicker_ReturnsNotFound()
    {
        var result = await Sut().GetFinancialFact("ZZZZ", "revenue");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task GetFinancialFact_UnknownConcept_ListsSupportedAliases()
    {
        DbContext.Set<EquityIssuer>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "ebitda");

        result.Should().Contain("Unknown concept 'ebitda'");
        result.Should().Contain("net-income");
    }

    [Fact]
    public async Task GetFinancialFact_NoFacts_ReturnsNotIngestedMessage()
    {
        DbContext.Set<EquityIssuer>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue");

        result.Should().Contain("No 'revenue' data has been ingested for AAPL");
    }

    [Theory]
    [InlineData("primary")]
    [InlineData("unresolved")]
    [InlineData("foreign")]
    public async Task GetFinancialFact_PerShareHistory_RestatesValuesFiledBeforeSplit(
        string splitScope
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "GOOGL",
            Name: "Alphabet Inc.",
            Cik: "0001652044"
        );
        var dilutedEps = AddConcept("EarningsPerShareDiluted");
        DbContext.Set<EquityIssuer>().Add(stock);
        var splitListingId = (Guid?)stock.Presentation.EquityListingId;
        if (splitScope == "unresolved")
            splitListingId = null;
        if (splitScope == "foreign")
        {
            var foreign = new EquityListing
            {
                EquitySecurityId = stock.Presentation.Listing.EquitySecurityId,
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
                    EquityIssuerId = stock.Id,
                    EquityListingId = splitListingId,
                    PriceSeriesTicker = splitScope == "unresolved" ? null : "GOOGL",
                    EffectiveDate = new DateOnly(2022, 7, 18),
                    Numerator = 20m,
                    Denominator = 1m,
                }
            );
        DbContext
            .Set<FinancialFact>()
            .AddRange(
                new FinancialFact
                {
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = dilutedEps.Id,
                    Unit = "USD/shares",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2022, 1, 1),
                    PeriodEnd = new DateOnly(2022, 3, 31),
                    Value = 24.62m,
                    FiscalYear = 2022,
                    FiscalPeriod = SecFiscalPeriod.Q1,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2022, 4, 26),
                    AccessionNumber = "googl-q1",
                },
                new FinancialFact
                {
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = dilutedEps.Id,
                    Unit = "USD/shares",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2022, 7, 1),
                    PeriodEnd = new DateOnly(2022, 9, 30),
                    Value = 1.06m,
                    FiscalYear = 2022,
                    FiscalPeriod = SecFiscalPeriod.Q3,
                    Form = DocumentType.TenQ,
                    FiledDate = new DateOnly(2022, 10, 26),
                    AccessionNumber = "googl-q3",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("GOOGL", "eps-diluted");

        result.Should().Contain("| $1.06 | USD/shares |", "the post-split filing stays unchanged");
        var expected =
            splitScope == "primary" ? "$1.23"
            : splitScope == "unresolved" ? "$24.62 (as filed)"
            : "$24.62";
        result.Should().Contain($"| {expected} | USD/shares |");
        if (splitScope == "primary")
            result.Should().Contain("are split-adjusted to today's share basis");
        else
            result.Should().NotContain("are split-adjusted to today's share basis");
        if (splitScope == "unresolved")
            result.Should().Contain("split attribution is unresolved");
        else
            result.Should().NotContain("split attribution is unresolved");
    }

    // A share count is stated on its filing date's basis: Alphabet's FY2021 count from the
    // FY2021 10-K (before the 20:1) multiplies; the FY2022 10-K's restated comparative stays.
    [Theory]
    [InlineData("2022-02-02", 662_000_000, "13,240,000,000", true)]
    [InlineData("2023-02-03", 13_242_000_000, "13,242,000,000", false)]
    public async Task GetFinancialFact_WeightedShareCount_RestatesFromItsFilingDate(
        string filed,
        long value,
        string expected,
        bool adjusted
    )
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "GOOGL",
            Name: "Alphabet Inc.",
            Cik: "0001652044"
        );
        var shares = AddConcept("WeightedAverageNumberOfDilutedSharesOutstanding");
        DbContext.Set<EquityIssuer>().Add(stock);
        DbContext
            .Set<StockSplit>()
            .Add(
                new StockSplit
                {
                    EquityIssuerId = stock.Id,
                    EquityListingId = stock.Presentation.EquityListingId,
                    PriceSeriesTicker = "GOOGL",
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
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = shares.Id,
                    Unit = "shares",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2021, 1, 1),
                    PeriodEnd = new DateOnly(2021, 12, 31),
                    Value = value,
                    FiscalYear = 2021,
                    FiscalPeriod = SecFiscalPeriod.FullYear,
                    Form = DocumentType.TenK,
                    FiledDate = DateOnly.Parse(filed),
                    AccessionNumber = "googl-fy",
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("GOOGL", "weighted-average-shares-diluted");

        result.Should().Contain($"| {expected} | shares |");
        if (adjusted)
            result.Should().Contain("are split-adjusted to today's share basis");
        else
            result.Should().NotContain("split-adjusted");
    }

    // A share count is identified by its as-of date. The 10-K cover count dated in January falls
    // in next year's Q1 beside the Q1 10-Q's own counts, and the balance-sheet count sits at each
    // period end; one row per fiscal period kept only the latest date and hid the rest.
    [Fact]
    public async Task GetFinancialFact_SharesOutstanding_PublishesEveryAsOfDate()
    {
        var stock = Apple();
        DbContext.Set<EquityIssuer>().Add(stock);
        var cover = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
            Label = "EntityCommonStockSharesOutstanding",
        };
        DbContext.Set<FinancialConcept>().Add(cover);
        var sheet = AddConcept("CommonStockSharesOutstanding");
        AddCount(
            stock,
            sheet,
            new DateOnly(2023, 12, 30),
            2023,
            SecFiscalPeriod.FullYear,
            15_550_061_000,
            DocumentType.TenK,
            "2024-02-02",
            "fy23"
        );
        AddCount(
            stock,
            cover,
            new DateOnly(2024, 1, 19),
            2024,
            SecFiscalPeriod.Q1,
            15_441_881_000,
            DocumentType.TenK,
            "2024-02-02",
            "fy23"
        );
        AddCount(
            stock,
            sheet,
            new DateOnly(2024, 3, 30),
            2024,
            SecFiscalPeriod.Q1,
            15_337_686_000,
            DocumentType.TenQ,
            "2024-05-03",
            "q1"
        );
        AddCount(
            stock,
            sheet,
            new DateOnly(2023, 12, 30),
            2024,
            SecFiscalPeriod.Q1,
            15_550_061_000,
            DocumentType.TenQ,
            "2024-05-03",
            "q1"
        );
        AddCount(
            stock,
            cover,
            new DateOnly(2024, 4, 19),
            2024,
            SecFiscalPeriod.Q1,
            15_284_155_000,
            DocumentType.TenQ,
            "2024-05-03",
            "q1"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "shares-outstanding");

        result.Should().Contain("| 2024-04-19 | 2024 | Q1 | 15,284,155,000 |");
        result.Should().Contain("| 2024-03-30 | 2024 | Q1 | 15,337,686,000 |");
        result.Should().Contain("| 2024-01-19 | 2024 | Q1 | 15,441,881,000 |");
        result
            .Should()
            .Contain(
                "| 2023-12-30 | 2023 | FY | 15,550,061,000 |",
                "a date keeps its own filing's stamp"
            );
        result.Split("15,550,061,000").Should().HaveCount(2, "one row per as-of date");
    }

    private void AddCount(
        EquityIssuer stock,
        FinancialConcept concept,
        DateOnly asOf,
        int fy,
        SecFiscalPeriod period,
        decimal value,
        DocumentType form,
        string filed,
        string accn
    ) =>
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = concept.Id,
                    Unit = "shares",
                    PeriodType = FactPeriodType.Instant,
                    PeriodStart = asOf,
                    PeriodEnd = asOf,
                    Value = value,
                    FiscalYear = fy,
                    FiscalPeriod = period,
                    Form = form,
                    FiledDate = DateOnly.Parse(filed),
                    AccessionNumber = accn,
                }
            );

    [Fact]
    public async Task GetFinancialFact_RestatementAndTagSwitch_LatestRestatedAcrossAliasTags()
    {
        EquityIssuer stock = Apple();
        DbContext.Set<EquityIssuer>().Add(stock);
        var revenues = AddConcept("Revenues");
        var asc606 = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        // FY2022 under the old tag, reported then restated.
        AddFact(
            stock,
            revenues,
            2022,
            SecFiscalPeriod.FullYear,
            300_000_000_000m,
            new DateOnly(2023, 1, 15),
            DocumentType.TenK,
            "a-2022-orig"
        );
        AddFact(
            stock,
            revenues,
            2022,
            SecFiscalPeriod.FullYear,
            320_000_000_000m,
            new DateOnly(2023, 8, 1),
            DocumentType.TenK,
            "a-2022-restate"
        );
        // FY2023 under the ASC 606 tag — same alias 'revenue'.
        AddFact(
            stock,
            asc606,
            2023,
            SecFiscalPeriod.FullYear,
            400_000_000_000m,
            new DateOnly(2024, 1, 15),
            DocumentType.TenK,
            "a-2023"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue");

        // Newest period first; the alias spans both tags.
        result.Should().Contain("| 2023-01-01 | 2023-12-31 | 2023 | FY | $400,000,000,000 |");
        result.Should().Contain("| 2022-01-01 | 2022-12-31 | 2022 | FY | $320,000,000,000 |");
        result.Should().NotContain("$300,000,000,000", "the latest restatement wins by default");
        var idx2023 = result.IndexOf("2023-12-31", StringComparison.Ordinal);
        var idx2022 = result.IndexOf("2022-12-31", StringComparison.Ordinal);
        idx2023.Should().BeLessThan(idx2022, "rows are ordered newest period first");
    }

    [Fact]
    public async Task GetFinancialFact_AsOriginallyReported_ShowsEarliestFiling()
    {
        EquityIssuer stock = Apple();
        DbContext.Set<EquityIssuer>().Add(stock);
        var revenues = AddConcept("Revenues");
        AddFact(
            stock,
            revenues,
            2022,
            SecFiscalPeriod.FullYear,
            300_000_000_000m,
            new DateOnly(2023, 1, 15),
            DocumentType.TenK,
            "a-orig"
        );
        AddFact(
            stock,
            revenues,
            2022,
            SecFiscalPeriod.FullYear,
            320_000_000_000m,
            new DateOnly(2023, 8, 1),
            DocumentType.TenK,
            "a-restate"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue", asOriginallyReported: true);

        result.Should().Contain("as originally reported");
        result.Should().Contain("$300,000,000,000");
        result.Should().NotContain("$320,000,000,000");
    }

    [Fact]
    public async Task GetFinancialFact_FormFilter_ReturnsOnlyMatchingForm()
    {
        EquityIssuer stock = Apple();
        DbContext.Set<EquityIssuer>().Add(stock);
        var revenues = AddConcept("Revenues");
        AddFact(
            stock,
            revenues,
            2023,
            SecFiscalPeriod.Q1,
            90_000_000_000m,
            new DateOnly(2023, 5, 1),
            DocumentType.TenQ,
            "q1"
        );
        AddFact(
            stock,
            revenues,
            2023,
            SecFiscalPeriod.FullYear,
            383_000_000_000m,
            new DateOnly(2024, 1, 15),
            DocumentType.TenK,
            "fy"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue", form: "10-K");

        result.Should().Contain("$383,000,000,000");
        result.Should().NotContain("$90,000,000,000", "the 10-Q row is filtered out");
    }

    [Fact]
    public async Task GetFinancialFact_SamePeriodBothAliasTags_PrimaryTagWinsDeterministically()
    {
        EquityIssuer stock = Apple();
        DbContext.Set<EquityIssuer>().Add(stock);
        var revenues = AddConcept("Revenues");
        var asc606 = AddConcept("RevenueFromContractWithCustomerExcludingAssessedTax");
        // ASC 606 transition: the SAME FY2023 period is tagged under both
        // concepts in the same filing (identical FiledDate). The alias lists
        // 'Revenues' first, so it must win deterministically.
        AddFact(
            stock,
            revenues,
            2023,
            SecFiscalPeriod.FullYear,
            383_000_000_000m,
            new DateOnly(2024, 1, 15),
            DocumentType.TenK,
            "a-2023"
        );
        AddFact(
            stock,
            asc606,
            2023,
            SecFiscalPeriod.FullYear,
            999_000_000_000m,
            new DateOnly(2024, 1, 15),
            DocumentType.TenK,
            "a-2023"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue");

        result.Should().Contain("$383,000,000,000", "the alias's primary tag (Revenues) wins");
        result
            .Should()
            .NotContain(
                "$999,000,000,000",
                "the secondary tag must not win a same-period, same-filed tie"
            );
    }

    [Fact]
    public async Task GetFinancialFact_LaterProxyDuplicate_DoesNotOutrankPeriodicReport()
    {
        EquityIssuer stock = Apple();
        DbContext.Set<EquityIssuer>().Add(stock);
        var revenues = AddConcept("Revenues");
        AddFact(
            stock,
            revenues,
            2023,
            SecFiscalPeriod.FullYear,
            383_285_000_000m,
            new DateOnly(2024, 2, 1),
            DocumentType.TenK,
            "ten-k"
        );
        AddFact(
            stock,
            revenues,
            2023,
            SecFiscalPeriod.FullYear,
            383_000_000_000m,
            new DateOnly(2024, 4, 1),
            DocumentType.Def14A,
            "proxy"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue");

        result.Should().Contain("$383,285,000,000");
        result.Should().Contain("| 10-K |");
        result.Should().NotContain("$383,000,000,000");
    }

    [Fact]
    public async Task GetFinancialFact_AliasLagsCompanyCorpus_AppendsCoverageWarning()
    {
        EquityIssuer stock = Apple();
        DbContext.Set<EquityIssuer>().Add(stock);
        var revenues = AddConcept("Revenues");
        var assets = AddConcept("Assets");
        AddFact(
            stock,
            revenues,
            2024,
            SecFiscalPeriod.FullYear,
            100_000_000m,
            new DateOnly(2025, 2, 1),
            DocumentType.TenK,
            "revenue-2024"
        );
        AddFact(
            stock,
            assets,
            2026,
            SecFiscalPeriod.Q2,
            500_000_000m,
            new DateOnly(2026, 8, 1),
            DocumentType.TenQ,
            "assets-2026"
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue");

        result.Should().Contain("Coverage warning: this alias ends 2024-12-31");
        result.Should().Contain("other structured facts for AAPL reach 2026-12-31");
        result.Should().Contain("may have changed XBRL tags");
    }

    [Fact]
    public async Task GetFinancialFact_InvalidDate_ReturnsGuidanceNotSilentlyUnfiltered()
    {
        DbContext.Set<EquityIssuer>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue", fromDate: "yesterday");

        result.Should().Contain("Unknown date 'yesterday'");
    }

    [Fact]
    public async Task GetFinancialFact_BlankConcept_ReturnsConceptRequired()
    {
        DbContext.Set<EquityIssuer>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "   ");

        result.Should().Contain("A concept is required");
    }
}
