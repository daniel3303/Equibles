using System.Text;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.BusinessLogic.Search.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Tests.Mcp;

// ═══════════════════════════════════════════════════════════════════════════
// RagSearchTools
// ═══════════════════════════════════════════════════════════════════════════

public class RagSearchToolsTests {
    private readonly IRagManager _ragManager;
    private readonly ISecDocumentService _secDocumentService;
    private readonly ErrorManager _errorManager;
    private readonly RagSearchTools _sut;

    public RagSearchToolsTests() {
        _ragManager = Substitute.For<IRagManager>();
        _secDocumentService = Substitute.For<ISecDocumentService>();
        _errorManager = Substitute.For<ErrorManager>((Equibles.Errors.Repositories.ErrorRepository)null);

        _sut = new RagSearchTools(
            _ragManager,
            _secDocumentService,
            _errorManager,
            Substitute.For<ILogger<RagSearchTools>>());
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static Chunk MakeChunk(
        string ticker = "AAPL", string companyName = "Apple Inc",
        string content = "Sample content", DocumentType documentType = null
    ) {
        var stock = new CommonStock { Ticker = ticker, Name = companyName };
        var doc = new Document {
            CommonStock = stock,
            CommonStockId = stock.Id,
            DocumentType = documentType ?? DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 3, 15),
        };
        return new Chunk {
            Document = doc,
            DocumentId = doc.Id,
            Content = content,
            DocumentType = doc.DocumentType,
            Ticker = ticker,
        };
    }

    private static SecDocumentInfo MakeDocumentInfo(
        string ticker = "AAPL", string companyName = "Apple Inc",
        DocumentType documentType = null
    ) {
        return new SecDocumentInfo {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            CompanyName = companyName,
            DocumentType = documentType ?? DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 3, 15),
            ReportingForDate = new DateOnly(2024, 12, 31),
            LineCount = 5000,
        };
    }

    // ── SearchDocuments ─────────────────────────────────────────────────

    [Fact]
    public async Task SearchDocuments_ResultsFound_ReturnsBuiltContext() {
        var chunks = new List<Chunk> { MakeChunk() };
        _ragManager.SearchRelevantChunks("revenue growth", 5, null, null, null)
            .Returns(chunks);
        _ragManager.BuildContext(chunks)
            .Returns("## Apple Inc (AAPL)\nRevenue grew 15%.");

        var result = await _sut.SearchDocuments("revenue growth");

        result.Should().Contain("Apple Inc (AAPL)");
        result.Should().Contain("Revenue grew 15%");
    }

    [Fact]
    public async Task SearchDocuments_NoResults_ReturnsNoDocumentsMessage() {
        _ragManager.SearchRelevantChunks(Arg.Any<string>(), Arg.Any<int>(),
                Arg.Any<DocumentType>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>())
            .Returns(new List<Chunk>());
        _ragManager.BuildContext(Arg.Is<List<Chunk>>(l => l.Count == 0))
            .Returns("No relevant financial documents found.");

        var result = await _sut.SearchDocuments("nonexistent topic");

        result.Should().Contain("No relevant financial documents found.");
    }

    [Fact]
    public async Task SearchDocuments_WithDocumentTypeFilter_PassesParsedType() {
        var chunks = new List<Chunk> { MakeChunk(documentType: DocumentType.TenQ) };
        _ragManager.SearchRelevantChunks("quarterly results", 5, DocumentType.TenQ, null, null)
            .Returns(chunks);
        _ragManager.BuildContext(chunks).Returns("Quarterly results context");

        var result = await _sut.SearchDocuments("quarterly results", documentType: "TenQ");

        result.Should().Contain("Quarterly results context");
        await _ragManager.Received(1).SearchRelevantChunks("quarterly results", 5, DocumentType.TenQ, null, null);
    }

    [Fact]
    public async Task SearchDocuments_WithDateFilters_PassesConvertedDates() {
        var start = new DateTime(2024, 1, 1);
        var end = new DateTime(2024, 12, 31);
        var startOnly = DateOnly.FromDateTime(start);
        var endOnly = DateOnly.FromDateTime(end);

        _ragManager.SearchRelevantChunks("earnings", 5, null, startOnly, endOnly)
            .Returns(new List<Chunk> { MakeChunk() });
        _ragManager.BuildContext(Arg.Any<List<Chunk>>()).Returns("Filtered results");

        var result = await _sut.SearchDocuments("earnings", startDate: start, endDate: end);

        result.Should().Contain("Filtered results");
        await _ragManager.Received(1).SearchRelevantChunks("earnings", 5, null, startOnly, endOnly);
    }

    [Fact]
    public async Task SearchDocuments_CustomMaxResults_PassedThrough() {
        _ragManager.SearchRelevantChunks("test", 10, null, null, null)
            .Returns(new List<Chunk>());
        _ragManager.BuildContext(Arg.Any<List<Chunk>>()).Returns("No results");

        await _sut.SearchDocuments("test", maxResults: 10);

        await _ragManager.Received(1).SearchRelevantChunks("test", 10, null, null, null);
    }

    // ── SearchCompanyDocuments ───────────────────────────────────────────

    [Fact]
    public async Task SearchCompanyDocuments_ResultsForTicker_ReturnsContext() {
        var chunks = new List<Chunk> { MakeChunk(ticker: "MSFT", companyName: "Microsoft Corp") };
        _ragManager.SearchRelevantChunksByCompany("cloud revenue", "MSFT", 5, null, null, null)
            .Returns(chunks);
        _ragManager.BuildContext(chunks).Returns("## Microsoft Corp (MSFT)\nCloud revenue data.");

        var result = await _sut.SearchCompanyDocuments("cloud revenue", "MSFT");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().Contain("Cloud revenue data");
    }

    [Fact]
    public async Task SearchCompanyDocuments_NoResults_ReturnsNoDocumentsMessage() {
        _ragManager.SearchRelevantChunksByCompany(Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<int>(), Arg.Any<DocumentType>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>())
            .Returns(new List<Chunk>());
        _ragManager.BuildContext(Arg.Is<List<Chunk>>(l => l.Count == 0))
            .Returns("No relevant financial documents found.");

        var result = await _sut.SearchCompanyDocuments("unknown topic", "ZZZZ");

        result.Should().Contain("No relevant financial documents found.");
    }

    [Fact]
    public async Task SearchCompanyDocuments_WithDocumentType_PassesParsedType() {
        _ragManager.SearchRelevantChunksByCompany("risk factors", "AAPL", 5, DocumentType.TenK, null, null)
            .Returns(new List<Chunk> { MakeChunk() });
        _ragManager.BuildContext(Arg.Any<List<Chunk>>()).Returns("Risk factors context");

        var result = await _sut.SearchCompanyDocuments("risk factors", "AAPL", documentType: "TenK");

        await _ragManager.Received(1)
            .SearchRelevantChunksByCompany("risk factors", "AAPL", 5, DocumentType.TenK, null, null);
    }

    // ── SearchDocument ──────────────────────────────────────────────────

    [Fact]
    public async Task SearchDocument_DocumentFound_ReturnsContext() {
        var docId = Guid.NewGuid();
        var chunks = new List<Chunk> { MakeChunk(content: "Revenue breakdown") };
        _ragManager.SearchRelevantChunksByDocument("revenue", docId, 5)
            .Returns(chunks);
        _ragManager.BuildContext(chunks).Returns("Revenue breakdown context");

        var result = await _sut.SearchDocument("revenue", docId);

        result.Should().Contain("Revenue breakdown context");
    }

    [Fact]
    public async Task SearchDocument_NotFound_ReturnsNoDocumentsMessage() {
        var docId = Guid.NewGuid();
        _ragManager.SearchRelevantChunksByDocument("query", docId, 5)
            .Returns(new List<Chunk>());
        _ragManager.BuildContext(Arg.Is<List<Chunk>>(l => l.Count == 0))
            .Returns("No relevant financial documents found.");

        var result = await _sut.SearchDocument("query", docId);

        result.Should().Contain("No relevant financial documents found.");
    }

    [Fact]
    public async Task SearchDocument_CustomMaxResults_PassedThrough() {
        var docId = Guid.NewGuid();
        _ragManager.SearchRelevantChunksByDocument("test", docId, 20)
            .Returns(new List<Chunk>());
        _ragManager.BuildContext(Arg.Any<List<Chunk>>()).Returns("No results");

        await _sut.SearchDocument("test", docId, maxResults: 20);

        await _ragManager.Received(1).SearchRelevantChunksByDocument("test", docId, 20);
    }

    // ── ListCompanyDocuments ────────────────────────────────────────────

    [Fact]
    public async Task ListCompanyDocuments_DocumentsFound_ReturnsFormattedTable() {
        var docs = new List<SecDocumentInfo> {
            MakeDocumentInfo(ticker: "AAPL", companyName: "Apple Inc"),
            MakeDocumentInfo(ticker: "AAPL", companyName: "Apple Inc", documentType: DocumentType.TenQ),
        };
        _secDocumentService.GetRecentDocuments("AAPL", null, null, 10, 1, null)
            .Returns(docs);

        var result = await _sut.ListCompanyDocuments("AAPL");

        result.Should().Contain("Financial documents for Apple Inc (AAPL)");
        result.Should().Contain("10-K");
        result.Should().Contain("10-Q");
        result.Should().Contain("page 1");
        result.Should().Contain(docs[0].Id.ToString());
        result.Should().Contain(docs[1].Id.ToString());
    }

    [Fact]
    public async Task ListCompanyDocuments_NoDocuments_ReturnsNotFoundMessage() {
        _secDocumentService.GetRecentDocuments("ZZZZ", null, null, 10, 1, null)
            .Returns(new List<SecDocumentInfo>());

        var result = await _sut.ListCompanyDocuments("ZZZZ");

        result.Should().Contain("No documents found for ticker ZZZZ");
    }

    [Fact]
    public async Task ListCompanyDocuments_ApplicationException_ReturnsExceptionMessage() {
        _secDocumentService.GetRecentDocuments("BAD", null, null, 10, 1, null)
            .Returns<List<SecDocumentInfo>>(x => throw new ApplicationException("Company BAD not found"));

        var result = await _sut.ListCompanyDocuments("BAD");

        result.Should().Contain("Company BAD not found");
    }

    [Fact]
    public async Task ListCompanyDocuments_Pagination_PassesPageNumber() {
        var docs = new List<SecDocumentInfo> { MakeDocumentInfo() };
        _secDocumentService.GetRecentDocuments("AAPL", null, null, 10, 3, null)
            .Returns(docs);

        var result = await _sut.ListCompanyDocuments("AAPL", page: 3);

        result.Should().Contain("page 3");
        await _secDocumentService.Received(1).GetRecentDocuments("AAPL", null, null, 10, 3, null);
    }

    [Fact]
    public async Task ListCompanyDocuments_WithDocumentTypeFilter_PassesParsedType() {
        _secDocumentService.GetRecentDocuments("AAPL", null, null, 10, 1, DocumentType.EightK)
            .Returns(new List<SecDocumentInfo> { MakeDocumentInfo(documentType: DocumentType.EightK) });

        var result = await _sut.ListCompanyDocuments("AAPL", documentType: "EightK");

        await _secDocumentService.Received(1).GetRecentDocuments("AAPL", null, null, 10, 1, DocumentType.EightK);
    }

    [Fact]
    public async Task ListCompanyDocuments_TableContainsHeaders() {
        var docs = new List<SecDocumentInfo> { MakeDocumentInfo() };
        _secDocumentService.GetRecentDocuments("AAPL", null, null, 10, 1, null)
            .Returns(docs);

        var result = await _sut.ListCompanyDocuments("AAPL");

        result.Should().Contain("ID | Type | Filed | Reporting For | Lines");
        result.Should().Contain("---|------|-------|---------------|------");
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DocumentTextTools
// ═══════════════════════════════════════════════════════════════════════════

public class DocumentTextToolsTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly DocumentRepository _documentRepository;
    private readonly DocumentTextTools _sut;

    public DocumentTextToolsTests() {
        _dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new MediaModuleConfiguration(),
            new SecTestModuleConfiguration());
        _documentRepository = new DocumentRepository(_dbContext);

        _sut = new DocumentTextTools(
            _documentRepository,
            Substitute.For<ErrorManager>((Equibles.Errors.Repositories.ErrorRepository)null),
            Substitute.For<ILogger<DocumentTextTools>>());
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private async Task<Document> SeedDocument(string content, string ticker = "AAPL", string companyName = "Apple Inc") {
        var stock = new CommonStock {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = companyName,
        };
        _dbContext.Set<CommonStock>().Add(stock);

        var fileContent = new FileContent {
            Bytes = Encoding.UTF8.GetBytes(content),
        };
        var file = new File {
            Id = Guid.NewGuid(),
            Name = "filing",
            Extension = "txt",
            ContentType = "text/plain",
            Size = fileContent.Bytes.Length,
            FileContent = fileContent,
        };
        fileContent.FileId = file.Id;
        _dbContext.Set<File>().Add(file);

        var document = new Document {
            Id = Guid.NewGuid(),
            CommonStock = stock,
            CommonStockId = stock.Id,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 3, 15),
            ReportingForDate = new DateOnly(2024, 12, 31),
            LineCount = content.Split('\n').Length,
        };
        _dbContext.Set<Document>().Add(document);
        await _dbContext.SaveChangesAsync();
        return document;
    }

    // ── SearchDocumentKeyword ───────────────────────────────────────────

    [Fact]
    public async Task SearchDocumentKeyword_KeywordFound_ReturnsMatchesWithContext() {
        var doc = await SeedDocument("Line one\nRevenue was $100M\nLine three\nRevenue increased\nLine five");

        var result = await _sut.SearchDocumentKeyword(doc.Id, "Revenue");

        result.Should().Contain("Revenue");
        result.Should().Contain("2 matches found");
        result.Should().Contain("**Revenue**");
    }

    [Fact]
    public async Task SearchDocumentKeyword_CaseInsensitive_FindsMatches() {
        var doc = await SeedDocument("First line\nTotal REVENUE was high\nLast line");

        var result = await _sut.SearchDocumentKeyword(doc.Id, "revenue");

        result.Should().Contain("1 matches found");
        result.Should().Contain("**REVENUE**");
    }

    [Fact]
    public async Task SearchDocumentKeyword_NotFound_ReturnsNoMatchesMessage() {
        var doc = await SeedDocument("Line one\nLine two\nLine three");

        var result = await _sut.SearchDocumentKeyword(doc.Id, "nonexistent");

        result.Should().Contain("No matches found for \"nonexistent\"");
        result.Should().Contain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchDocumentKeyword_DocumentNotFound_ReturnsNotFoundMessage() {
        var missingId = Guid.NewGuid();

        var result = await _sut.SearchDocumentKeyword(missingId, "test");

        result.Should().Contain($"Document {missingId} not found.");
    }

    [Fact]
    public async Task SearchDocumentKeyword_MaxResultsRespected_LimitsMatches() {
        var lines = Enumerable.Range(1, 50)
            .Select(i => $"Revenue line {i}")
            .ToArray();
        var doc = await SeedDocument(string.Join("\n", lines));

        var result = await _sut.SearchDocumentKeyword(doc.Id, "Revenue", maxResults: 3);

        result.Should().Contain("3 matches found");
    }

    [Fact]
    public async Task SearchDocumentKeyword_IncludesContextLines() {
        var doc = await SeedDocument("Before line\nTarget keyword here\nAfter line");

        var result = await _sut.SearchDocumentKeyword(doc.Id, "keyword");

        result.Should().Contain("Before line");
        result.Should().Contain("**keyword**");
        result.Should().Contain("After line");
    }

    [Fact]
    public async Task SearchDocumentKeyword_IncludesDocumentMetadata() {
        var doc = await SeedDocument("Some keyword content", ticker: "MSFT", companyName: "Microsoft Corp");

        var result = await _sut.SearchDocumentKeyword(doc.Id, "keyword");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().Contain("10-K");
        result.Should().Contain("2025-03-15");
    }

    // ── ReadDocumentLines ───────────────────────────────────────────────

    [Fact]
    public async Task ReadDocumentLines_ValidRange_ReturnsNumberedLines() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3\nLine 4\nLine 5");

        var result = await _sut.ReadDocumentLines(doc.Id, 2, 4);

        result.Should().Contain("Line 2");
        result.Should().Contain("Line 3");
        result.Should().Contain("Line 4");
        result.Should().NotContain("Line 1");
        result.Should().NotContain("Line 5");
        result.Should().Contain("lines 2 to 4 of 5");
    }

    [Fact]
    public async Task ReadDocumentLines_EntireDocument_ReturnsAllLines() {
        var doc = await SeedDocument("Alpha\nBravo\nCharlie");

        var result = await _sut.ReadDocumentLines(doc.Id, 1, 3);

        result.Should().Contain("Alpha");
        result.Should().Contain("Bravo");
        result.Should().Contain("Charlie");
        result.Should().Contain("lines 1 to 3 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_StartLineBelowOne_ClampedToOne() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await _sut.ReadDocumentLines(doc.Id, -5, 2);

        result.Should().Contain("Line 1");
        result.Should().Contain("Line 2");
        result.Should().Contain("lines 1 to 2 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_EndLineBeyondTotal_ClampedToTotal() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await _sut.ReadDocumentLines(doc.Id, 2, 100);

        result.Should().Contain("Line 2");
        result.Should().Contain("Line 3");
        result.Should().Contain("lines 2 to 3 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_StartAfterEnd_ReturnsInvalidRangeMessage() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        // startLine=5 > totalLines=3, so after clamping: startLine=5, endLine=3 → invalid
        var result = await _sut.ReadDocumentLines(doc.Id, 5, 2);

        result.Should().Contain("Invalid line range");
    }

    [Fact]
    public async Task ReadDocumentLines_DocumentNotFound_ReturnsNotFoundMessage() {
        var missingId = Guid.NewGuid();

        var result = await _sut.ReadDocumentLines(missingId, 1, 10);

        result.Should().Contain($"Document {missingId} not found.");
    }

    [Fact]
    public async Task ReadDocumentLines_IncludesDocumentMetadata() {
        var doc = await SeedDocument("Content here", ticker: "GOOG", companyName: "Alphabet Inc");

        var result = await _sut.ReadDocumentLines(doc.Id, 1, 1);

        result.Should().Contain("Alphabet Inc (GOOG)");
        result.Should().Contain("10-K");
        result.Should().Contain("2025-03-15");
    }

    [Fact]
    public async Task ReadDocumentLines_LinesAreNumbered() {
        var doc = await SeedDocument("Alpha\nBravo\nCharlie");

        var result = await _sut.ReadDocumentLines(doc.Id, 1, 3);

        // Lines are formatted with line numbers and │ separator
        result.Should().Contain("1 │ Alpha");
        result.Should().Contain("2 │ Bravo");
        result.Should().Contain("3 │ Charlie");
    }

    [Fact]
    public async Task ReadDocumentLines_SingleLine_ReturnsOneLine() {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await _sut.ReadDocumentLines(doc.Id, 2, 2);

        result.Should().Contain("Line 2");
        result.Should().Contain("lines 2 to 2 of 3");
    }
}
