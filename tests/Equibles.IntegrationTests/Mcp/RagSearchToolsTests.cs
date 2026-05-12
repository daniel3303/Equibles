using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Mcp;

// ═══════════════════════════════════════════════════════════════════════════
// RagSearchTools
// ═══════════════════════════════════════════════════════════════════════════
//
// These tests exercise the FULL RAG path against a real ParadeDB container:
// Chunk seeding → pg_search BM25 index → EF.Functions.Parse / EF.Functions.Score
// in ChunkRepository.HybridSearch → RagManager → RagSearchTools. The InMemory
// EF provider can't run pg_search at all, so pre-Postgres-fixture versions of
// these tests had to mock IRagManager entirely. Now we assert on the real
// BM25 ranking — a regression in the index, the query rewrite, or the score
// extraction shows up here, not in production.

[Collection(ParadeDbCollection.Name)]
public class RagSearchToolsTests : ParadeDbMcpTestBase {
    public RagSearchToolsTests(ParadeDbFixture fixture) : base(fixture) { }

    private RagSearchTools Sut() {
        var ragManager = new RagManager(
            new ChunkRepository(DbContext),
            new CommonStockRepository(DbContext),
            NullLogger<RagManager>());
        var secDocumentService = new SecDocumentService(
            new DocumentRepository(DbContext),
            NullLogger<SecDocumentService>());
        return new RagSearchTools(ragManager, secDocumentService, ErrorManager, NullLogger<RagSearchTools>());
    }

    // ── Seeding ─────────────────────────────────────────────────────────

    private async Task<(CommonStock stock, Document document, List<Chunk> chunks)> SeedDocumentWithChunks(
        string[] chunkContents,
        string ticker = "AAPL", string companyName = "Apple Inc",
        DocumentType documentType = null, DateOnly? reportingDate = null
    ) {
        documentType ??= DocumentType.TenK;
        var docReportingDate = reportingDate ?? new DateOnly(2026, 3, 15);

        // Ticker has a unique index; reuse an existing stock if a previous call in the same
        // test already seeded one with this ticker.
        var stockSet = DbContext.Set<CommonStock>();
        var stock = stockSet.Local.FirstOrDefault(s => s.Ticker == ticker)
            ?? await stockSet.FirstOrDefaultAsync(s => s.Ticker == ticker);
        if (stock == null) {
            stock = new CommonStock { Ticker = ticker, Name = companyName, Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString() };
            stockSet.Add(stock);
        }

        var fileContent = new FileContent { Bytes = "placeholder"u8.ToArray() };
        var file = new File {
            Name = "filing", Extension = "txt", ContentType = "text/plain",
            Size = fileContent.Bytes.Length, FileContent = fileContent,
        };
        fileContent.FileId = file.Id;
        DbContext.Set<File>().Add(file);

        var document = new Document {
            CommonStock = stock, CommonStockId = stock.Id,
            Content = file, ContentId = file.Id,
            DocumentType = documentType,
            ReportingDate = docReportingDate,
            ReportingForDate = docReportingDate.AddDays(-30),
            LineCount = chunkContents.Length,
        };
        DbContext.Set<Document>().Add(document);

        var chunks = new List<Chunk>();
        for (var i = 0; i < chunkContents.Length; i++) {
            var content = chunkContents[i];
            chunks.Add(new Chunk {
                Document = document, DocumentId = document.Id,
                Index = i, StartPosition = i * 100, EndPosition = i * 100 + content.Length,
                StartLineNumber = i + 1, Content = content,
                DocumentType = documentType, Ticker = ticker,
                ReportingDate = DateTime.SpecifyKind(
                    docReportingDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            });
        }
        DbContext.Set<Chunk>().AddRange(chunks);
        await DbContext.SaveChangesAsync();

        return (stock, document, chunks);
    }

    // ── SearchDocuments ─────────────────────────────────────────────────

    [Fact]
    public async Task SearchDocuments_NoMatches_ReturnsNoDocumentsMessage() {
        await SeedDocumentWithChunks(
            chunkContents: new[] { "We make consumer electronics.", "Smartphones drive revenue." });

        var result = await Sut().SearchDocuments("blockchain cryptocurrency mining");

        // BM25 search returns no rows → RagManager.BuildContext returns the standard empty message.
        result.Should().Be("No relevant financial documents found.");
    }

    [Fact]
    public async Task SearchDocuments_MatchByKeyword_BuildsContextWithCompanyAndContent() {
        await SeedDocumentWithChunks(
            chunkContents: new[] {
                "Apple's services segment grew 15% year-over-year, driven by App Store revenue.",
                "We design and manufacture smartphones, computers, and tablets.",
            });

        var result = await Sut().SearchDocuments("services segment revenue", maxResults: 5);

        // The first chunk should match on "services", "segment", and "revenue".
        result.Should().Contain("Apple Inc (AAPL)");
        result.Should().Contain("services segment grew 15%");
        result.Should().Contain("10-K");
    }

    [Fact]
    public async Task SearchDocuments_FiltersByDocumentType_ExcludesOtherTypes() {
        await SeedDocumentWithChunks(
            documentType: DocumentType.TenK, ticker: "AAPL",
            chunkContents: new[] { "Annual report discussing quarterly results breakdown." });
        await SeedDocumentWithChunks(
            documentType: DocumentType.TenQ, ticker: "MSFT", companyName: "Microsoft Corp",
            chunkContents: new[] { "Quarterly results show steady growth." });

        var result = await Sut().SearchDocuments("quarterly results", documentType: "TenQ");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().NotContain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchDocuments_FiltersByDateRange_ExcludesOutsideWindow() {
        await SeedDocumentWithChunks(
            ticker: "AAPL", reportingDate: new DateOnly(2024, 1, 15),
            chunkContents: new[] { "Revenue increased substantially in fiscal year 2024." });
        await SeedDocumentWithChunks(
            ticker: "MSFT", companyName: "Microsoft Corp", reportingDate: new DateOnly(2026, 3, 1),
            chunkContents: new[] { "Revenue increased substantially in fiscal year 2026." });

        var result = await Sut().SearchDocuments("revenue increased fiscal year",
            startDate: new DateTime(2026, 1, 1), endDate: new DateTime(2026, 12, 31));

        result.Should().Contain("MSFT");
        result.Should().NotContain("AAPL");
    }

    // ── SearchCompanyDocuments ──────────────────────────────────────────

    [Fact]
    public async Task SearchCompanyDocuments_OnlyReturnsRequestedTicker() {
        await SeedDocumentWithChunks(
            ticker: "AAPL", chunkContents: new[] { "Cloud revenue increased." });
        await SeedDocumentWithChunks(
            ticker: "MSFT", companyName: "Microsoft Corp",
            chunkContents: new[] { "Cloud revenue increased substantially." });

        var result = await Sut().SearchCompanyDocuments("cloud revenue", "MSFT");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().NotContain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchCompanyDocuments_UnknownTicker_StillReturnsEmptyMessage() {
        await SeedDocumentWithChunks(
            ticker: "AAPL", chunkContents: new[] { "Cloud revenue increased." });

        // ResolvePrimaryTicker falls back to the input ticker on miss; BM25 then finds zero rows
        // because no chunk has Ticker = "ZZZZ".
        var result = await Sut().SearchCompanyDocuments("cloud revenue", "ZZZZ");

        result.Should().Be("No relevant financial documents found.");
    }

    // ── SearchDocument ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchDocument_RestrictsSearchToProvidedDocumentId() {
        var (_, docOne, _) = await SeedDocumentWithChunks(
            ticker: "AAPL", chunkContents: new[] { "Risk factor: supply chain disruption." });
        var (_, docTwo, _) = await SeedDocumentWithChunks(
            ticker: "MSFT", companyName: "Microsoft Corp",
            chunkContents: new[] { "Risk factor: supply chain disruption." });

        var result = await Sut().SearchDocument("supply chain", docTwo.Id);

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().NotContain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchDocument_UnknownDocumentId_ReturnsEmptyMessage() {
        await SeedDocumentWithChunks(chunkContents: new[] { "Some content." });

        var result = await Sut().SearchDocument("anything", Guid.NewGuid());

        result.Should().Be("No relevant financial documents found.");
    }

    // ── ListCompanyDocuments ────────────────────────────────────────────

    [Fact]
    public async Task ListCompanyDocuments_UnknownTicker_ReturnsNotFoundMessage() {
        var result = await Sut().ListCompanyDocuments("ZZZZ");

        result.Should().Contain("No documents found for ticker ZZZZ");
    }

    [Fact]
    public async Task ListCompanyDocuments_RendersDocumentsForCompany() {
        await SeedDocumentWithChunks(
            ticker: "AAPL", documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2026, 3, 15), chunkContents: new[] { "Annual report content." });
        await SeedDocumentWithChunks(
            ticker: "AAPL", documentType: DocumentType.TenQ,
            reportingDate: new DateOnly(2026, 4, 30), chunkContents: new[] { "Quarterly content." });

        var result = await Sut().ListCompanyDocuments("AAPL");

        result.Should().Contain("Financial documents for Apple Inc (AAPL)");
        result.Should().Contain("10-K");
        result.Should().Contain("10-Q");
        result.Should().Contain("page 1");
        result.Should().Contain("ID | Type | Filed | Reporting For | Lines");
    }

    [Fact]
    public async Task ListCompanyDocuments_OrdersNewestFirst() {
        await SeedDocumentWithChunks(
            ticker: "AAPL", reportingDate: new DateOnly(2025, 6, 30),
            chunkContents: new[] { "Older filing." });
        await SeedDocumentWithChunks(
            ticker: "AAPL", documentType: DocumentType.TenQ,
            reportingDate: new DateOnly(2026, 4, 30), chunkContents: new[] { "Newer filing." });

        var result = await Sut().ListCompanyDocuments("AAPL");

        result.IndexOf("2026-04-30").Should().BeLessThan(result.IndexOf("2025-06-30"));
    }

    [Fact]
    public async Task ListCompanyDocuments_FiltersByDocumentType() {
        await SeedDocumentWithChunks(
            ticker: "AAPL", documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2026, 3, 15), chunkContents: new[] { "Annual." });
        await SeedDocumentWithChunks(
            ticker: "AAPL", documentType: DocumentType.EightK,
            reportingDate: new DateOnly(2026, 4, 10), chunkContents: new[] { "Current report." });

        var result = await Sut().ListCompanyDocuments("AAPL", documentType: "EightK");

        result.Should().Contain("8-K");
        result.Should().NotContain("10-K");
    }

    [Fact]
    public async Task ListCompanyDocuments_PaginatesAcrossPages() {
        // Seed 12 docs so pages 1 and 2 each have content; page 2 should return docs 11-12.
        for (var i = 0; i < 12; i++) {
            await SeedDocumentWithChunks(
                ticker: "AAPL",
                reportingDate: new DateOnly(2026, 1, 1).AddDays(i),
                chunkContents: new[] { $"Doc {i + 1}" });
        }

        var page1 = await Sut().ListCompanyDocuments("AAPL", page: 1);
        var page2 = await Sut().ListCompanyDocuments("AAPL", page: 2);

        page1.Should().Contain("page 1");
        page2.Should().Contain("page 2");
        // Page 1 holds 10 newest (Jan 12..Jan 3); page 2 holds 2 oldest (Jan 2, Jan 1).
        page1.Should().Contain("2026-01-12");
        page2.Should().Contain("2026-01-01");
        page2.Should().NotContain("2026-01-12");
    }
}
