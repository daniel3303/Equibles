using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Mcp;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Mcp.Captures;
using Equibles.Sec.FinancialFacts.Mcp.Tools;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class FinancialToolsNewerEarningsReleaseTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly QuarterEnd = new(2026, 7, 26);
    private static readonly DateOnly QuarterlyReportFiled = new(2026, 8, 26);

    public FinancialToolsNewerEarningsReleaseTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private FinancialFactsTools Facts() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialFactsTools>(),
            new DocumentRepository(DbContext)
        );

    private FinancialStatementTools Statements() =>
        new(
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            new StockSplitRepository(DbContext),
            ErrorManager,
            NullLogger<FinancialStatementTools>(),
            new DocumentRepository(DbContext)
        );

    // One issuer whose newest structured facts are a 10-Q's quarter revenue and inventory balance.
    private async Task<EquityIssuer> SeedIssuerWithQuarterlyReport()
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "NVDA",
            Name: "Nvidia Corp",
            Cik: "0001045810"
        );
        DbContext.Set<EquityIssuer>().Add(stock);
        var inventory = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "InventoryNet",
            Label = "Inventory",
        };
        DbContext.Set<FinancialConcept>().Add(inventory);
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = inventory.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Instant,
                    PeriodStart = QuarterEnd,
                    PeriodEnd = QuarterEnd,
                    Value = 31_575_000_000m,
                    FiscalYear = 2026,
                    FiscalPeriod = SecFiscalPeriod.Q2,
                    Form = DocumentType.TenQ,
                    FiledDate = QuarterlyReportFiled,
                    AccessionNumber = "0001045810-26-000075",
                }
            );
        var revenue = new FinancialConcept
        {
            Id = Guid.NewGuid(),
            Taxonomy = FactTaxonomy.UsGaap,
            Tag = "Revenues",
            Label = "Revenues",
        };
        DbContext.Set<FinancialConcept>().Add(revenue);
        DbContext
            .Set<FinancialFact>()
            .Add(
                new FinancialFact
                {
                    Id = Guid.NewGuid(),
                    EquityIssuerId = stock.Id,
                    FinancialConceptId = revenue.Id,
                    Unit = "USD",
                    PeriodType = FactPeriodType.Duration,
                    PeriodStart = new DateOnly(2026, 4, 27),
                    PeriodEnd = QuarterEnd,
                    Value = 96_200_000_000m,
                    FiscalYear = 2026,
                    FiscalPeriod = SecFiscalPeriod.Q2,
                    Form = DocumentType.TenQ,
                    FiledDate = QuarterlyReportFiled,
                    AccessionNumber = "0001045810-26-000075",
                }
            );
        await DbContext.SaveChangesAsync();
        return stock;
    }

    private async Task<Document> SeedCurrentReport(EquityIssuer stock, DateOnly filed, string items)
    {
        var fileContent = new FileContent { Bytes = "placeholder"u8.ToArray() };
        var file = new File
        {
            Name = "filing",
            Extension = "txt",
            ContentType = "text/plain",
            Size = fileContent.Bytes.Length,
            FileContent = fileContent,
        };
        fileContent.FileId = file.Id;
        DbContext.Set<File>().Add(file);
        var document = new Document
        {
            EquityIssuerId = stock.Id,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.EightK,
            ReportingDate = filed,
            ReportingForDate = filed,
            Items = items,
            LineCount = 1,
        };
        DbContext.Set<Document>().Add(document);
        await DbContext.SaveChangesAsync();
        return document;
    }

    [Fact]
    public async Task GetFinancialFact_ReleaseFiledAfterTheLatestPeriodicReport_NamesItsDocument()
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        var release = await SeedCurrentReport(stock, new DateOnly(2026, 11, 17), "2.02,9.01");

        var result = await Facts().GetFinancialFact("NVDA", "inventory");

        result.Should().Contain("31,575,000,000");
        result.Should().Contain("An earnings release was filed 2026-11-17");
        result.Should().Contain(release.Id.ToString());
        result.Should().Contain("filed 2026-08-26, period ended 2026-07-26");
    }

    [Fact]
    public async Task GetFinancialFact_NothingInTheRequestedWindow_StillNamesTheRelease()
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        var release = await SeedCurrentReport(stock, new DateOnly(2026, 11, 17), "2.02,9.01");

        var result = await Facts().GetFinancialFact("NVDA", "inventory", fromDate: "2026-10-01");

        result.Should().Contain("No 'inventory' data found for NVDA");
        result.Should().Contain(release.Id.ToString());
    }

    [Fact]
    public async Task GetFinancialFact_WindowEndingAtTheLatestReportedPeriod_OmitsTheRelease()
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        await SeedCurrentReport(stock, new DateOnly(2026, 11, 17), "2.02,9.01");

        var result = await Facts().GetFinancialFact("NVDA", "inventory", toDate: "2026-07-26");

        result.Should().Contain("31,575,000,000");
        result.Should().NotContain("earnings release was filed");
    }

    [Theory]
    // Filed with the periodic report: the report already carries the release's period.
    [InlineData("2026-08-26", "2.02,9.01")]
    // Filed the day after the periodic report, before another quarter could have ended: it
    // restates the quarter the report already covers.
    [InlineData("2026-08-27", "2.02,9.01")]
    // A later current report that is not a results release.
    [InlineData("2026-11-12", "5.02,9.01")]
    // Item 12.02 does not exist, but a substring match on 2.02 would accept it.
    [InlineData("2026-11-17", "12.02")]
    public async Task GetFinancialFact_NoLaterResultsRelease_AddsNoNote(string filed, string items)
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        await SeedCurrentReport(stock, DateOnly.Parse(filed), items);

        var result = await Facts().GetFinancialFact("NVDA", "inventory");

        result.Should().Contain("31,575,000,000");
        result.Should().NotContain("earnings release was filed");
    }

    [Fact]
    public async Task GetFinancialStatement_LatestPeriod_NamesTheNewerRelease()
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        var release = await SeedCurrentReport(stock, new DateOnly(2026, 11, 17), "2.02,9.01");

        var result = await Statements().GetFinancialStatement("NVDA", statement: "balance");

        result.Should().Contain("Inventory");
        result.Should().Contain(release.Id.ToString());
    }

    [Fact]
    public async Task GetFinancialStatement_EarlierFiscalYear_OmitsTheRelease()
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        await SeedCurrentReport(stock, new DateOnly(2026, 11, 17), "2.02,9.01");

        var result = await Statements()
            .GetFinancialStatement("NVDA", statement: "balance", year: 2025);

        result.Should().NotContain("earnings release was filed");
    }

    // A card reader can neither open a document by its ID nor call a tool, so the card names the release only.
    private const string ReleaseCardNote =
        "An earnings release filed 2026-11-17 may report a later period than these figures.";

    [Fact]
    public async Task GetFinancialFact_NewerRelease_CardNoteNamesTheRelease()
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        await SeedCurrentReport(stock, new DateOnly(2026, 11, 17), "2.02,9.01");

        using var scope = McpResultCapture.Begin();
        await Facts().GetFinancialFact("NVDA", "inventory");

        scope.Get<FinancialFactHistoryCapture>().Notes.Should().Contain(ReleaseCardNote);
    }

    [Fact]
    public async Task GetFinancialStatement_NewerRelease_CardNoteNamesTheRelease()
    {
        var stock = await SeedIssuerWithQuarterlyReport();
        await SeedCurrentReport(stock, new DateOnly(2026, 11, 17), "2.02,9.01");

        using var scope = McpResultCapture.Begin();
        await Statements().GetFinancialStatement("NVDA", statement: "balance");

        scope.Get<FinancialStatementCapture>().Notes.Should().Contain(ReleaseCardNote);
    }
}
