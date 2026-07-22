using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
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
public class RagSearchToolsTests : ParadeDbMcpTestBase
{
    public RagSearchToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private RagSearchTools Sut()
    {
        var ragManager = new RagManager(
            HybridChunkSearcherFactory.Bm25Only(DbContext),
            new CommonStockRepository(DbContext),
            NullLogger<RagManager>()
        );
        var secDocumentService = new SecDocumentService(
            new DocumentRepository(DbContext),
            NullLogger<SecDocumentService>()
        );
        // Exact mode reads the document text through IFileManager; serve the seeded
        // FileContent bytes so searchMode "exact" scans the same content the DB holds.
        var fileManager = Substitute.For<IFileManager>();
        fileManager.GetContent(Arg.Any<File>()).Returns(ci => ((File)ci[0]).FileContent.Bytes);
        return new RagSearchTools(
            ragManager,
            secDocumentService,
            new CommonStockRepository(DbContext),
            new DocumentRepository(DbContext),
            fileManager,
            ErrorManager,
            NullLogger<RagSearchTools>()
        );
    }

    // ── Seeding ─────────────────────────────────────────────────────────

    private async Task<(
        CommonStock stock,
        Document document,
        List<Chunk> chunks
    )> SeedDocumentWithChunks(
        string[] chunkContents,
        string ticker = "AAPL",
        string companyName = "Apple Inc",
        DocumentType documentType = null,
        DateOnly? reportingDate = null
    )
    {
        documentType ??= DocumentType.TenK;
        var docReportingDate = reportingDate ?? new DateOnly(2026, 3, 15);

        // Ticker has a unique index; reuse an existing stock if a previous call in the same
        // test already seeded one with this ticker.
        var stockSet = DbContext.Set<CommonStock>();
        var stock =
            stockSet.Local.FirstOrDefault(s => s.Ticker == ticker)
            ?? await stockSet.FirstOrDefaultAsync(s => s.Ticker == ticker);
        if (stock == null)
        {
            stock = new CommonStock
            {
                Ticker = ticker,
                Name = companyName,
                Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
            };
            stockSet.Add(stock);
        }

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
            CommonStock = stock,
            CommonStockId = stock.Id,
            Content = file,
            ContentId = file.Id,
            DocumentType = documentType,
            ReportingDate = docReportingDate,
            ReportingForDate = docReportingDate.AddDays(-30),
            LineCount = chunkContents.Length,
        };
        DbContext.Set<Document>().Add(document);

        var chunks = new List<Chunk>();
        for (var i = 0; i < chunkContents.Length; i++)
        {
            var content = chunkContents[i];
            chunks.Add(
                new Chunk
                {
                    Document = document,
                    DocumentId = document.Id,
                    Index = i,
                    StartPosition = i * 100,
                    EndPosition = i * 100 + content.Length,
                    StartLineNumber = i + 1,
                    Content = content,
                    DocumentType = documentType,
                    Ticker = ticker,
                    ReportingDate = DateTime.SpecifyKind(
                        docReportingDate.ToDateTime(TimeOnly.MinValue),
                        DateTimeKind.Utc
                    ),
                }
            );
        }
        DbContext.Set<Chunk>().AddRange(chunks);
        await DbContext.SaveChangesAsync();

        return (stock, document, chunks);
    }

    // ── SearchDocuments ─────────────────────────────────────────────────

    [Fact]
    public async Task SearchDocuments_NoMatches_ReturnsNoDocumentsMessage()
    {
        await SeedDocumentWithChunks(
            chunkContents: new[] { "We make consumer electronics.", "Smartphones drive revenue." }
        );

        var result = await Sut().SearchDocuments("blockchain cryptocurrency mining");

        // BM25 search returns no rows → RagManager.BuildContext returns the standard empty message.
        result.Should().Be("No relevant financial documents found.");
    }

    [Fact]
    public async Task SearchDocuments_MatchByKeyword_BuildsContextWithCompanyAndContent()
    {
        await SeedDocumentWithChunks(
            chunkContents: new[]
            {
                "Apple's services segment grew 15% year-over-year, driven by App Store revenue.",
                "We design and manufacture smartphones, computers, and tablets.",
            }
        );

        var result = await Sut().SearchDocuments("services segment revenue", maxResults: 5);

        // The first chunk should match on "services", "segment", and "revenue".
        result.Should().Contain("Apple Inc (AAPL)");
        result.Should().Contain("services segment grew 15%");
        result.Should().Contain("10-K");
    }

    [Fact]
    public async Task SearchDocuments_FiltersByDocumentType_ExcludesOtherTypes()
    {
        await SeedDocumentWithChunks(
            documentType: DocumentType.TenK,
            ticker: "AAPL",
            chunkContents: new[] { "Annual report discussing quarterly results breakdown." }
        );
        await SeedDocumentWithChunks(
            documentType: DocumentType.TenQ,
            ticker: "MSFT",
            companyName: "Microsoft Corp",
            chunkContents: new[] { "Quarterly results show steady growth." }
        );

        var result = await Sut().SearchDocuments("quarterly results", documentType: "TenQ");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().NotContain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchDocuments_FiltersByDateRange_ExcludesOutsideWindow()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            reportingDate: new DateOnly(2024, 1, 15),
            chunkContents: new[] { "Revenue increased substantially in fiscal year 2024." }
        );
        await SeedDocumentWithChunks(
            ticker: "MSFT",
            companyName: "Microsoft Corp",
            reportingDate: new DateOnly(2026, 3, 1),
            chunkContents: new[] { "Revenue increased substantially in fiscal year 2026." }
        );

        var result = await Sut()
            .SearchDocuments(
                "revenue increased fiscal year",
                startDate: new DateTime(2026, 1, 1),
                endDate: new DateTime(2026, 12, 31)
            );

        result.Should().Contain("MSFT");
        result.Should().NotContain("AAPL");
    }

    [Fact]
    public async Task SearchDocuments_NegativeMaxResults_DoesNotSurfaceInternalError()
    {
        await SeedDocumentWithChunks(
            chunkContents: new[]
            {
                "Apple's services segment grew 15% year-over-year, driven by App Store revenue.",
            }
        );

        // Contract: maxResults comes straight from an untrusted MCP client. Unclamped it flows
        // into ChunkRepository.HybridSearch's .Take(maxResults), so a negative value reaches
        // PostgreSQL as a negative LIMIT, which the engine rejects. The tool must clamp it and
        // degrade gracefully rather than leak the executor's internal-error sentinel.
        var result = await Sut().SearchDocuments("services segment revenue", maxResults: -1);

        result.Should().NotContain("An error occurred while executing");
        // McpLimit.Clamp floors a non-positive cap at 1 (never turns "has data" into "no data"),
        // so the seeded document is still returned rather than a false empty-result message.
        result.Should().NotBe("No relevant financial documents found.");
        result.Should().Contain("services segment");
    }

    // ── SearchCompanyDocuments ──────────────────────────────────────────

    [Fact]
    public async Task SearchCompanyDocuments_OnlyReturnsRequestedTicker()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            chunkContents: new[] { "Cloud revenue increased." }
        );
        await SeedDocumentWithChunks(
            ticker: "MSFT",
            companyName: "Microsoft Corp",
            chunkContents: new[] { "Cloud revenue increased substantially." }
        );

        var result = await Sut().SearchCompanyDocuments("cloud revenue", "MSFT");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().NotContain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchCompanyDocuments_UnknownTicker_ReturnsStockNotFound()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            chunkContents: new[] { "Cloud revenue increased." }
        );

        // A mistyped ticker must be distinguishable from "this company's filings say
        // nothing about the topic": the tool checks the stock exists before searching.
        var result = await Sut().SearchCompanyDocuments("cloud revenue", "ZZZZ");

        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task SearchCompanyDocuments_UnknownDocumentType_ReturnsAcceptedValues()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            chunkContents: new[] { "Cloud revenue increased." }
        );

        // A near-miss type ('10K', 'Transcript') must not silently search unfiltered —
        // the caller would present mixed-type results as filtered ones.
        var result = await Sut()
            .SearchCompanyDocuments("cloud revenue", "AAPL", documentType: "10K");

        result.Should().StartWith("Unknown documentType '10K'.");
        result.Should().Contain("'TenK' (10-K)");
    }

    [Fact]
    public async Task SearchDocuments_InvertedDateRange_ReturnsExplicitError()
    {
        await SeedDocumentWithChunks(chunkContents: new[] { "Revenue increased." });

        // start > end must not collapse into the generic empty-result message — the
        // caller would conclude no such documents exist.
        var result = await Sut()
            .SearchDocuments(
                "revenue",
                startDate: new DateTime(2026, 1, 1),
                endDate: new DateTime(2020, 1, 1)
            );

        result.Should().Be("startDate 2026-01-01 is after endDate 2020-01-01 — swap the values.");
    }

    [Fact]
    public async Task SearchDocuments_ResultHeaders_IncludeDocumentId()
    {
        var (_, document, _) = await SeedDocumentWithChunks(
            chunkContents: new[] { "Services segment revenue grew strongly." }
        );

        var result = await Sut().SearchDocuments("services segment revenue");

        // The ID feeds SearchDocument/ReadDocumentLines directly — without it the caller
        // must re-derive it from ListCompanyDocuments by fuzzy type+date matching.
        result.Should().Contain($"(ID: {document.Id})");
    }

    // ── SearchDocument ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchDocument_RestrictsSearchToProvidedDocumentId()
    {
        var (_, docOne, _) = await SeedDocumentWithChunks(
            ticker: "AAPL",
            chunkContents: new[] { "Risk factor: supply chain disruption." }
        );
        var (_, docTwo, _) = await SeedDocumentWithChunks(
            ticker: "MSFT",
            companyName: "Microsoft Corp",
            chunkContents: new[] { "Risk factor: supply chain disruption." }
        );

        var result = await Sut().SearchDocument("supply chain", docTwo.Id);

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().NotContain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchDocument_UnknownDocumentId_ReturnsDocumentNotFound()
    {
        await SeedDocumentWithChunks(chunkContents: new[] { "Some content." });

        var missingId = Guid.NewGuid();
        var result = await Sut().SearchDocument("anything", missingId);

        // A stale or mistyped ID must not read as "this filing says nothing about the
        // topic" — the caller needs to know the ID itself is wrong.
        result
            .Should()
            .Be(
                $"Document {missingId} not found — obtain a valid document ID from ListCompanyDocuments."
            );
    }

    [Fact]
    public async Task SearchDocument_ExistingDocumentNoMatches_ReturnsNoExcerptsMessage()
    {
        var (_, document, _) = await SeedDocumentWithChunks(
            chunkContents: new[] { "Consumer electronics revenue." }
        );

        var result = await Sut().SearchDocument("blockchain cryptocurrency mining", document.Id);

        // Same empty outcome, different cause: the document exists but has no matching
        // content — distinguishable from the bad-ID case above.
        result.Should().StartWith("No matching excerpts found in this document");
        result.Should().Contain("10-K");
    }

    [Fact]
    public async Task SearchDocument_WordyQueryWithOneNonMatchingToken_StillReturnsExcerpts()
    {
        // Conjunctive BM25 ANDs every token: "drivers" appears nowhere in the chunk, so
        // the strict pass returns nothing. The document-scoped tool opts into the
        // disjunctive fallback, which must recover the on-point chunk.
        var (_, document, _) = await SeedDocumentWithChunks(
            chunkContents: new[]
            {
                "Revenue from Data Center computing grew 59% driven by demand.",
                "Gaming revenue was flat compared to the prior year.",
            }
        );

        var result = await Sut().SearchDocument("data center revenue growth drivers", document.Id);

        result.Should().Contain("Data Center computing grew");
    }

    [Fact]
    public async Task SearchDocument_ExactMode_ReturnsLinePreciseKeywordMatches()
    {
        // Exact mode must run the SearchDocumentKeyword scan over the document TEXT (via
        // IFileManager), not the chunk index — precise line numbers, literal substring.
        var (_, document, _) = await SeedDocumentWithChunks(
            chunkContents: new[] { "irrelevant chunk" }
        );
        document.Content.FileContent.Bytes = Encoding.UTF8.GetBytes(
            "First line of the filing.\nTotal revenue was $6.62 billion.\nClosing remarks."
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchDocument("Total revenue", document.Id, searchMode: "exact");

        result.Should().Contain("Keyword search for \"Total revenue\"");
        result.Should().Contain("1 matching line(s)");
        // The scan's line format: width-6 right-aligned number + box-drawing bar.
        result.Should().Contain("     2 │ **Total revenue** was $6.62 billion.");
    }

    [Fact]
    public async Task SearchDocument_ExactModeNoMatches_ReturnsKeywordNoMatchMessage()
    {
        var (_, document, _) = await SeedDocumentWithChunks(
            chunkContents: new[] { "Consumer electronics revenue." }
        );
        document.Content.FileContent.Bytes = Encoding.UTF8.GetBytes("Nothing relevant here.");
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .SearchDocument("Digital Media ARR", document.Id, searchMode: "exact");

        // The exact-mode empty outcome is the keyword scan's message, not the semantic
        // "no matching excerpts" one — the caller can tell which mode actually ran.
        result.Should().StartWith("No matches found for \"Digital Media ARR\"");
    }

    [Fact]
    public async Task SearchDocument_UnknownSearchMode_ReturnsCorrectiveError()
    {
        var (_, document, _) = await SeedDocumentWithChunks(
            chunkContents: new[] { "Some content." }
        );

        var result = await Sut().SearchDocument("anything", document.Id, searchMode: "fuzzy");

        // An unknown mode must never silently fall back to semantic search — the caller
        // would misread relevance-ranked excerpts as exact-match results.
        result
            .Should()
            .Be("Unknown searchMode \"fuzzy\" — pass 'semantic' (default) or 'exact'.");
    }

    // ── ListCompanyDocuments ────────────────────────────────────────────

    [Fact]
    public async Task ListCompanyDocuments_UnknownTicker_ReturnsStockNotFound()
    {
        var result = await Sut().ListCompanyDocuments("ZZZZ");

        // An unknown ticker is not the same empty state as "known company, nothing
        // ingested" — the caller must be told the ticker itself missed.
        result.Should().Be("Stock 'ZZZZ' not found.");
    }

    [Fact]
    public async Task ListCompanyDocuments_KnownTickerNoDocuments_ReturnsNoDocumentsMessage()
    {
        DbContext
            .Set<CommonStock>()
            .Add(
                new CommonStock
                {
                    Ticker = "NODOC",
                    Name = "Empty Corp",
                    Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
                }
            );
        await DbContext.SaveChangesAsync();

        var result = await Sut().ListCompanyDocuments("NODOC");

        result.Should().Contain("No documents found for ticker NODOC");
    }

    [Fact]
    public async Task ListCompanyDocuments_FiltersExcludeEverything_SaysDocumentsExistWithoutThem()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2026, 3, 15),
            chunkContents: new[] { "Annual report content." }
        );

        var result = await Sut()
            .ListCompanyDocuments(
                "AAPL",
                startDate: new DateTime(1990, 1, 1),
                endDate: new DateTime(1990, 12, 31)
            );

        // Distinguish "the filters excluded everything" from "nothing is ingested".
        result.Should().Contain("No documents match the given filters for AAPL");
        result.Should().Contain("1 document(s) exist without them");
    }

    [Fact]
    public async Task ListCompanyDocuments_UnknownDocumentType_ReturnsAcceptedValues()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            chunkContents: new[] { "Annual report content." }
        );

        // The old lenient behavior returned an UNFILTERED list for a near-miss like
        // 'AnnualReport' — indistinguishable from a correctly filtered one.
        var result = await Sut().ListCompanyDocuments("AAPL", documentType: "AnnualReport");

        result.Should().StartWith("Unknown documentType 'AnnualReport'.");
        result.Should().Contain("'TenK' (10-K)");
    }

    [Fact]
    public async Task ListCompanyDocuments_PageZero_ReturnsExplicitError()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            chunkContents: new[] { "Annual report content." }
        );

        // page=0 used to flow into Skip(-maxItems) — a negative OFFSET PostgreSQL rejects,
        // surfacing as the generic internal-error sentinel.
        var result = await Sut().ListCompanyDocuments("AAPL", page: 0);

        result.Should().Be("Invalid page 0 — pages are numbered from 1.");
    }

    [Fact]
    public async Task ListCompanyDocuments_PagePastEnd_ReturnsOutOfRangeMessage()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            chunkContents: new[] { "Annual report content." }
        );

        // Paging past the end used to return "No documents found for ticker AAPL" even
        // though documents plainly exist — the caller would conclude the company has none.
        var result = await Sut().ListCompanyDocuments("AAPL", page: 5);

        result.Should().Contain("Page 5 is out of range");
        result.Should().Contain("1 matching document(s)");
    }

    [Fact]
    public async Task ListCompanyDocuments_HiddenDocumentType_ExcludedUnlessExplicitlyRequested()
    {
        // Registration is process-global and idempotent (TryAdd); the type is test-only
        // and no other test filters on it.
        var hiddenType = new DocumentType(
            "TestHiddenNews",
            "Test Hidden News",
            hiddenFromFilingLists: true
        );
        DocumentType.Register(hiddenType);

        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2026, 3, 15),
            chunkContents: new[] { "Annual report content." }
        );
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: hiddenType,
            reportingDate: new DateOnly(2026, 4, 1),
            chunkContents: new[] { "News item content." }
        );

        var unfiltered = await Sut().ListCompanyDocuments("AAPL");
        var explicitlyRequested = await Sut()
            .ListCompanyDocuments("AAPL", documentType: "TestHiddenNews");

        // Hidden types are news, not filings: they must not crowd the unfiltered list,
        // but an explicit request for the type still returns them.
        unfiltered.Should().Contain("10-K");
        unfiltered.Should().NotContain("Test Hidden News");
        unfiltered.Should().Contain("(1 documents)");
        explicitlyRequested.Should().Contain("Test Hidden News");
        explicitlyRequested.Should().NotContain("10-K");
    }

    [Fact]
    public async Task ListCompanyDocuments_RendersDocumentsForCompany()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2026, 3, 15),
            chunkContents: new[] { "Annual report content." }
        );
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: DocumentType.TenQ,
            reportingDate: new DateOnly(2026, 4, 30),
            chunkContents: new[] { "Quarterly content." }
        );

        var result = await Sut().ListCompanyDocuments("AAPL");

        result.Should().Contain("Financial documents for Apple Inc (AAPL)");
        result.Should().Contain("10-K");
        result.Should().Contain("10-Q");
        result.Should().Contain("page 1");
        result.Should().Contain("ID | Type | Filed | Reporting For | Lines");
    }

    [Fact]
    public async Task ListCompanyDocuments_OrdersNewestFirst()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            reportingDate: new DateOnly(2025, 6, 30),
            chunkContents: new[] { "Older filing." }
        );
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: DocumentType.TenQ,
            reportingDate: new DateOnly(2026, 4, 30),
            chunkContents: new[] { "Newer filing." }
        );

        var result = await Sut().ListCompanyDocuments("AAPL");

        result.IndexOf("2026-04-30").Should().BeLessThan(result.IndexOf("2025-06-30"));
    }

    [Fact]
    public async Task ListCompanyDocuments_FiltersByDocumentType()
    {
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: DocumentType.TenK,
            reportingDate: new DateOnly(2026, 3, 15),
            chunkContents: new[] { "Annual." }
        );
        await SeedDocumentWithChunks(
            ticker: "AAPL",
            documentType: DocumentType.EightK,
            reportingDate: new DateOnly(2026, 4, 10),
            chunkContents: new[] { "Current report." }
        );

        var result = await Sut().ListCompanyDocuments("AAPL", documentType: "EightK");

        result.Should().Contain("8-K");
        result.Should().NotContain("10-K");
    }

    [Fact]
    public async Task ListCompanyDocuments_PaginatesAcrossPages()
    {
        // Seed 12 docs so pages 1 and 2 each have content; page 2 should return docs 11-12.
        for (var i = 0; i < 12; i++)
        {
            await SeedDocumentWithChunks(
                ticker: "AAPL",
                reportingDate: new DateOnly(2026, 1, 1).AddDays(i),
                chunkContents: new[] { $"Doc {i + 1}" }
            );
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
