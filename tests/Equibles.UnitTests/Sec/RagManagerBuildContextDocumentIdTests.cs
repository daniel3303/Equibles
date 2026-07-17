using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.UnitTests.Sec;

// BuildContext render additions shipped with the MCP documents-search audit:
// (1) includeDocumentIds stamps the document GUID into each per-document header so the
//     MCP search tools' results feed SearchDocument/ReadDocumentLines directly;
// (2) grouping keys on Document.Id, so two distinct same-type same-day filings render as
//     two documents instead of merging under one header (which would attach one ID to
//     excerpts of another document);
// (3) image-markdown references are stripped at render time (slide-deck captures embed
//     one per slide — tokens without information for a text consumer);
// (4) maxExcerptChars caps each excerpt with an explicit truncation note, never silently.
public class RagManagerBuildContextDocumentIdTests
{
    private static RagManager Sut() =>
        new(hybridChunkSearcher: null, commonStockRepository: null, logger: null);

    private static Document NewDocument(Guid? id = null)
    {
        var document = new Document
        {
            CommonStock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 12, 31),
        };
        if (id.HasValue)
        {
            document.Id = id.Value;
        }
        return document;
    }

    private static Chunk NewChunk(Document document, string content, int index = 0) =>
        new()
        {
            Index = index,
            StartPosition = index * 100,
            StartLineNumber = index + 1,
            Content = content,
            Document = document,
        };

    [Fact]
    public async Task BuildContext_IncludeDocumentIds_StampsIdIntoDocumentHeader()
    {
        var documentId = Guid.NewGuid();
        var document = NewDocument(documentId);

        var result = await Sut()
            .BuildContext([NewChunk(document, "Revenue grew.")], includeDocumentIds: true);

        result.Should().Contain($"**Document:** 10-K filed on 2024-12-31 (ID: {documentId})");
    }

    [Fact]
    public async Task BuildContext_DefaultArguments_OmitsDocumentId()
    {
        var document = NewDocument(Guid.NewGuid());

        var result = await Sut().BuildContext([NewChunk(document, "Revenue grew.")]);

        result.Should().NotContain("(ID:");
        result.Should().Contain("**Document:** 10-K filed on 2024-12-31");
    }

    [Fact]
    public async Task BuildContext_TwoSameTypeSameDayDocuments_RenderAsSeparateGroups()
    {
        var docOne = NewDocument(Guid.NewGuid());
        var docTwo = NewDocument(Guid.NewGuid());

        var result = await Sut()
            .BuildContext(
                [
                    NewChunk(docOne, "First filing content."),
                    NewChunk(docTwo, "Second filing content."),
                ],
                includeDocumentIds: true
            );

        // Grouping by (ticker, type, date) alone would merge the two filings under one
        // header — and attach one document's ID to the other's excerpts.
        result.Should().Contain($"(ID: {docOne.Id})");
        result.Should().Contain($"(ID: {docTwo.Id})");
    }

    [Fact]
    public async Task BuildContext_ImageMarkdownInContent_IsStrippedAtRenderTime()
    {
        var document = NewDocument();
        var chunk = NewChunk(
            document,
            "Revenue grew. ![](investorpresentationfina015.jpg \"slide14\") Margins expanded."
        );

        var result = await Sut().BuildContext([chunk]);

        result.Should().NotContain("investorpresentationfina015.jpg");
        result.Should().Contain("Revenue grew.");
        result.Should().Contain("Margins expanded.");
    }

    [Fact]
    public async Task BuildContext_MaxExcerptChars_TruncatesAtWordBoundaryWithNote()
    {
        var document = NewDocument();
        var chunk = NewChunk(
            document,
            "Revenue from the Data Center segment grew substantially during the fiscal year."
        );

        var result = await Sut().BuildContext([chunk], maxExcerptChars: 30);

        result.Should().Contain("truncated");
        result.Should().NotContain("during the fiscal year");
        // Cut lands on a word boundary, never mid-word.
        result.Should().Contain("Revenue from the Data Center");
    }

    [Fact]
    public async Task BuildContext_MaxExcerptCharsZero_ReturnsFullExcerpt()
    {
        var document = NewDocument();
        var content = "Revenue grew substantially during the fiscal year under review.";

        var result = await Sut().BuildContext([NewChunk(document, content)], maxExcerptChars: 0);

        result.Should().Contain(content);
        result.Should().NotContain("truncated");
    }
}
