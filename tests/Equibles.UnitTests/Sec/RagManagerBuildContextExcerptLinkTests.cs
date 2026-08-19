using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.UnitTests.Sec;

// The optional excerpt-link seam: when a deployment registers an IDocumentExcerptLinkBuilder
// AND the caller opts in with includeExcerptLinks, each rendered excerpt gains a trailing
// "Link: <url>" line anchored on the chunk's line span. Everything else must stay
// byte-identical — the flag defaults to false and the builder to null, so existing callers
// (and deployments without a public viewer) render exactly what they rendered before.
public class RagManagerBuildContextExcerptLinkTests
{
    private sealed class RecordingLinkBuilder : IDocumentExcerptLinkBuilder
    {
        public Document Document;
        public int FromLine;
        public int ToLine;
        public string ExcerptText;
        public string Url = "https://example.com/doc";

        public string BuildExcerptUrl(
            Document document,
            int fromLine,
            int toLine,
            string excerptText
        )
        {
            Document = document;
            FromLine = fromLine;
            ToLine = toLine;
            ExcerptText = excerptText;
            return Url;
        }
    }

    private static RagManager Sut(IDocumentExcerptLinkBuilder linkBuilder = null) =>
        new(
            hybridChunkSearcher: null,
            commonStockRepository: null,
            logger: null,
            excerptLinkBuilder: linkBuilder
        );

    private static Document NewDocument() =>
        new()
        {
            Id = Guid.NewGuid(),
            CommonStock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 12, 31),
        };

    private static Chunk NewChunk(Document document, string content, int startLineNumber = 10) =>
        new()
        {
            Index = 0,
            StartPosition = 0,
            StartLineNumber = startLineNumber,
            Content = content,
            Document = document,
        };

    [Fact]
    public async Task BuildContext_OptedInWithBuilder_AppendsLinkLineAfterExcerptContent()
    {
        var builder = new RecordingLinkBuilder();
        var chunk = NewChunk(NewDocument(), "Revenue grew.");

        var result = await Sut(builder).BuildContext([chunk], includeExcerptLinks: true);

        result.Should().Contain("Revenue grew.\nLink: https://example.com/doc\n");
    }

    [Fact]
    public async Task BuildContext_LineSpanCoversTheChunk_AndTextIsTheRawChunkContent()
    {
        var builder = new RecordingLinkBuilder();
        var content = "line one\nline two\nline three";
        var chunk = NewChunk(NewDocument(), content, startLineNumber: 42);

        await Sut(builder).BuildContext([chunk], includeExcerptLinks: true);

        builder.FromLine.Should().Be(42);
        builder.ToLine.Should().Be(44, "toLine is the start line plus the content's newlines");
        builder.ExcerptText.Should().Be(content, "the builder truncates and encodes itself");
        builder.Document.Should().BeSameAs(chunk.Document);
    }

    [Fact]
    public async Task BuildContext_DefaultArguments_StaysLinkFreeEvenWithABuilder()
    {
        var builder = new RecordingLinkBuilder();
        var chunk = NewChunk(NewDocument(), "Revenue grew.");

        var result = await Sut(builder).BuildContext([chunk]);

        result.Should().NotContain("Link:");
    }

    [Fact]
    public async Task BuildContext_OptedInWithoutABuilder_StaysLinkFree()
    {
        var chunk = NewChunk(NewDocument(), "Revenue grew.");

        var result = await Sut().BuildContext([chunk], includeExcerptLinks: true);

        result.Should().NotContain("Link:");
    }

    [Fact]
    public async Task BuildContext_ChunkWithoutALineNumber_GetsNoLink()
    {
        var builder = new RecordingLinkBuilder();
        var chunk = NewChunk(NewDocument(), "Revenue grew.", startLineNumber: 0);

        var result = await Sut(builder).BuildContext([chunk], includeExcerptLinks: true);

        result.Should().NotContain("Link:");
    }

    [Fact]
    public async Task BuildContext_BuilderDeclines_GetsNoLinkLine()
    {
        var builder = new RecordingLinkBuilder { Url = null };
        var chunk = NewChunk(NewDocument(), "Revenue grew.");

        var result = await Sut(builder).BuildContext([chunk], includeExcerptLinks: true);

        result.Should().NotContain("Link:");
    }
}
