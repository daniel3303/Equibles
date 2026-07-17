using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class DocumentTextToolsTests : ParadeDbMcpTestBase
{
    public DocumentTextToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private DocumentTextTools Sut()
    {
        var fileManager = Substitute.For<IFileManager>();
        fileManager.GetContent(Arg.Any<File>()).Returns(ci => ((File)ci[0]).FileContent.Bytes);
        return new(
            new DocumentRepository(DbContext),
            ErrorManager,
            fileManager,
            NullLogger<DocumentTextTools>()
        );
    }

    private async Task<Document> SeedDocument(
        string content,
        string ticker = "AAPL",
        string companyName = "Apple Inc"
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = companyName,
            Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
        };
        DbContext.Set<CommonStock>().Add(stock);

        var fileContent = new FileContent { Bytes = Encoding.UTF8.GetBytes(content) };
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
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2025, 3, 15),
            ReportingForDate = new DateOnly(2024, 12, 31),
            LineCount = content.Split('\n').Length,
        };
        DbContext.Set<Document>().Add(document);
        await DbContext.SaveChangesAsync();
        return document;
    }

    // ── SearchDocumentKeyword ───────────────────────────────────────────

    [Fact]
    public async Task SearchDocumentKeyword_KeywordFound_ReturnsMatchesWithContext()
    {
        var doc = await SeedDocument(
            "Line one\nRevenue was $100M\nLine three\nRevenue increased\nLine five"
        );

        var result = await Sut().SearchDocumentKeyword(doc.Id, "Revenue");

        result.Should().Contain("Revenue");
        result.Should().Contain("2 matching line(s)");
        result.Should().Contain("**Revenue**");
    }

    [Fact]
    public async Task SearchDocumentKeyword_CaseInsensitive_FindsMatches()
    {
        var doc = await SeedDocument("First line\nTotal REVENUE was high\nLast line");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "revenue");

        result.Should().Contain("1 matching line(s)");
        result.Should().Contain("**REVENUE**");
    }

    [Fact]
    public async Task SearchDocumentKeyword_NotFound_ReturnsNoMatchesMessage()
    {
        var doc = await SeedDocument("Line one\nLine two\nLine three");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "nonexistent");

        result.Should().Contain("No matches found for \"nonexistent\"");
        result.Should().Contain("Apple Inc (AAPL)");
    }

    [Fact]
    public async Task SearchDocumentKeyword_DocumentNotFound_ReturnsNotFoundMessage()
    {
        var missingId = Guid.NewGuid();

        var result = await Sut().SearchDocumentKeyword(missingId, "test");

        result.Should().Contain($"Document {missingId} not found.");
    }

    [Fact]
    public async Task SearchDocumentKeyword_MaxResultsRespected_ReportsTrueTotalAndTruncation()
    {
        var lines = Enumerable.Range(1, 50).Select(i => $"Revenue line {i}").ToArray();
        var doc = await SeedDocument(string.Join("\n", lines));

        var result = await Sut().SearchDocumentKeyword(doc.Id, "Revenue", maxResults: 3);

        // The header must report the TRUE total, not the capped count: "3 matches found"
        // for a keyword that appears 50 times makes the caller state a wrong count.
        result.Should().Contain("50 matching line(s)");
        result.Should().Contain("Showing first 3 of 50");
        result.Should().NotContain("Revenue line 5\n".TrimEnd());
    }

    [Fact]
    public async Task SearchDocumentKeyword_TypographicApostropheInDocument_MatchesAsciiKeyword()
    {
        // Stored filings carry smart punctuation (U+2019); callers type ASCII. Without
        // the typography fold this search silently returns "No matches found" for text
        // the document visibly contains.
        var doc = await SeedDocument("First line\nThe world’s largest supplier\nLast line");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "world's largest");

        result.Should().Contain("1 matching line(s)");
        result.Should().Contain("**world’s largest**");
    }

    [Fact]
    public async Task SearchDocumentKeyword_AdjacentMatches_DoNotRepeatContextLines()
    {
        // Two consecutive matching lines used to print two overlapping 3-line blocks,
        // duplicating the shared lines; merged blocks print each line at most once.
        var doc = await SeedDocument("Alpha\nRevenue one\nRevenue two\nOmega");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "Revenue");

        CountOccurrences(result, "Alpha").Should().Be(1);
        CountOccurrences(result, "Omega").Should().Be(1);
        CountOccurrences(result, "**Revenue** one").Should().Be(1);
        CountOccurrences(result, "**Revenue** two").Should().Be(1);
    }

    [Fact]
    public async Task SearchDocumentKeyword_NonPositiveMaxResults_StillReturnsAMatch()
    {
        // maxResults=0 used to Take(0) every real match and then claim "No matches
        // found" — a factually wrong message. The clamp floors it at 1.
        var doc = await SeedDocument("Revenue line\nOther line");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "Revenue", maxResults: 0);

        result.Should().NotContain("No matches found");
        result.Should().Contain("**Revenue**");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    [Fact]
    public async Task SearchDocumentKeyword_IncludesContextLines()
    {
        var doc = await SeedDocument("Before line\nTarget keyword here\nAfter line");

        var result = await Sut().SearchDocumentKeyword(doc.Id, "keyword");

        result.Should().Contain("Before line");
        result.Should().Contain("**keyword**");
        result.Should().Contain("After line");
    }

    [Fact]
    public async Task SearchDocumentKeyword_IncludesDocumentMetadata()
    {
        var doc = await SeedDocument(
            "Some keyword content",
            ticker: "MSFT",
            companyName: "Microsoft Corp"
        );

        var result = await Sut().SearchDocumentKeyword(doc.Id, "keyword");

        result.Should().Contain("Microsoft Corp (MSFT)");
        result.Should().Contain("10-K");
        result.Should().Contain("2025-03-15");
    }

    // ── ReadDocumentLines ───────────────────────────────────────────────

    [Fact]
    public async Task ReadDocumentLines_ValidRange_ReturnsNumberedLines()
    {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3\nLine 4\nLine 5");

        var result = await Sut().ReadDocumentLines(doc.Id, 2, 4);

        result.Should().Contain("Line 2");
        result.Should().Contain("Line 3");
        result.Should().Contain("Line 4");
        result.Should().NotContain("Line 1");
        result.Should().NotContain("Line 5");
        result.Should().Contain("lines 2 to 4 of 5");
    }

    [Fact]
    public async Task ReadDocumentLines_EntireDocument_ReturnsAllLines()
    {
        var doc = await SeedDocument("Alpha\nBravo\nCharlie");

        var result = await Sut().ReadDocumentLines(doc.Id, 1, 3);

        result.Should().Contain("Alpha");
        result.Should().Contain("Bravo");
        result.Should().Contain("Charlie");
        result.Should().Contain("lines 1 to 3 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_StartLineBelowOne_ClampedToOne()
    {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, -5, 2);

        result.Should().Contain("Line 1");
        result.Should().Contain("Line 2");
        result.Should().Contain("lines 1 to 2 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_EndLineBeyondTotal_ClampedToTotal()
    {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, 2, 100);

        result.Should().Contain("Line 2");
        result.Should().Contain("Line 3");
        result.Should().Contain("lines 2 to 3 of 3");
    }

    [Fact]
    public async Task ReadDocumentLines_StartAfterEnd_ReturnsInvalidRangeMessage()
    {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, 5, 2);

        result.Should().Contain("Invalid line range");
    }

    [Fact]
    public async Task ReadDocumentLines_DocumentNotFound_ReturnsNotFoundMessage()
    {
        var missingId = Guid.NewGuid();

        var result = await Sut().ReadDocumentLines(missingId, 1, 10);

        result.Should().Contain($"Document {missingId} not found.");
    }

    [Fact]
    public async Task ReadDocumentLines_IncludesDocumentMetadata()
    {
        var doc = await SeedDocument("Content here", ticker: "GOOG", companyName: "Alphabet Inc");

        var result = await Sut().ReadDocumentLines(doc.Id, 1, 1);

        result.Should().Contain("Alphabet Inc (GOOG)");
        result.Should().Contain("10-K");
        result.Should().Contain("2025-03-15");
    }

    [Fact]
    public async Task ReadDocumentLines_LinesAreNumbered()
    {
        var doc = await SeedDocument("Alpha\nBravo\nCharlie");

        var result = await Sut().ReadDocumentLines(doc.Id, 1, 3);

        result.Should().Contain("1 │ Alpha");
        result.Should().Contain("2 │ Bravo");
        result.Should().Contain("3 │ Charlie");
    }

    [Fact]
    public async Task ReadDocumentLines_SingleLine_ReturnsOneLine()
    {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, 2, 2);

        result.Should().Contain("Line 2");
        result.Should().Contain("lines 2 to 2 of 3");
        result.Should().NotContain("Line 1");
        result.Should().NotContain("Line 3");
    }

    [Fact]
    public async Task ReadDocumentLines_RangeBeyondCap_TruncatesWithContinuationNote()
    {
        // Prod documents reach 500k+ lines; an uncapped range request would return the
        // whole document in one MCP response. The cap must be honest and self-describing.
        var lines = Enumerable.Range(1, 2050).Select(i => $"Row {i}").ToArray();
        var doc = await SeedDocument(string.Join("\n", lines));

        var result = await Sut().ReadDocumentLines(doc.Id, 1, 999_999);

        result.Should().Contain("lines 1 to 2,000 of 2,050");
        result.Should().Contain("Row 2000");
        result.Should().NotContain("Row 2001\n".TrimEnd());
        result.Should().Contain("continue with startLine=2,001");
    }

    [Fact]
    public async Task ReadDocumentLines_InvertedRange_QuotesOriginalArguments()
    {
        var doc = await SeedDocument("Line 1\nLine 2\nLine 3");

        var result = await Sut().ReadDocumentLines(doc.Id, 50, 10);

        // The message must quote the caller's own values, not clamped ones.
        result.Should().Be("Invalid line range: 50 to 10 — startLine is after endLine.");
    }
}
