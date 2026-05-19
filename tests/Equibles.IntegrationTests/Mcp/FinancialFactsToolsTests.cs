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
public class FinancialFactsToolsTests : ParadeDbMcpTestBase
{
    public FinancialFactsToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools Sut() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>()
        );

    private static CommonStock Apple() =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };

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
                    CommonStockId = stock.Id,
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
        DbContext.Set<CommonStock>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "ebitda");

        result.Should().Contain("Unknown concept 'ebitda'");
        result.Should().Contain("net-income");
    }

    [Fact]
    public async Task GetFinancialFact_NoFacts_ReturnsNotIngestedMessage()
    {
        DbContext.Set<CommonStock>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue");

        result.Should().Contain("No 'revenue' data has been ingested for AAPL");
    }

    [Fact]
    public async Task GetFinancialFact_RestatementAndTagSwitch_LatestRestatedAcrossAliasTags()
    {
        var stock = Apple();
        DbContext.Set<CommonStock>().Add(stock);
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
        result.Should().Contain("| 2023-12-31 | 2023 | FY | $400,000,000,000 |");
        result.Should().Contain("| 2022-12-31 | 2022 | FY | $320,000,000,000 |");
        result.Should().NotContain("$300,000,000,000", "the latest restatement wins by default");
        var idx2023 = result.IndexOf("2023-12-31", StringComparison.Ordinal);
        var idx2022 = result.IndexOf("2022-12-31", StringComparison.Ordinal);
        idx2023.Should().BeLessThan(idx2022, "rows are ordered newest period first");
    }

    [Fact]
    public async Task GetFinancialFact_AsOriginallyReported_ShowsEarliestFiling()
    {
        var stock = Apple();
        DbContext.Set<CommonStock>().Add(stock);
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
        var stock = Apple();
        DbContext.Set<CommonStock>().Add(stock);
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
        var stock = Apple();
        DbContext.Set<CommonStock>().Add(stock);
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
    public async Task GetFinancialFact_InvalidDate_ReturnsGuidanceNotSilentlyUnfiltered()
    {
        DbContext.Set<CommonStock>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "revenue", fromDate: "yesterday");

        result.Should().Contain("Unknown date 'yesterday'");
    }

    [Fact]
    public async Task GetFinancialFact_BlankConcept_ReturnsConceptRequired()
    {
        DbContext.Set<CommonStock>().Add(Apple());
        await DbContext.SaveChangesAsync();

        var result = await Sut().GetFinancialFact("AAPL", "   ");

        result.Should().Contain("A concept is required");
    }
}
