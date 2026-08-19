using System.Reflection;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

// The keyword scan's per-block excerpt links: with a link builder present, each merged
// grep-style block ends with ONE "Link: <url>" line anchored on the block's rendered line
// span (1-based) and the block's first MATCHED line's text — never a context line's. With
// no builder (the framework default) the rendering is byte-identical to before the seam
// existed.
public class DocumentKeywordScanBlockLinkTests
{
    private sealed class RecordingLinkBuilder : IDocumentExcerptLinkBuilder
    {
        public readonly List<(int FromLine, int ToLine, string ExcerptText)> Calls = [];

        public string BuildExcerptUrl(
            Document document,
            int fromLine,
            int toLine,
            string excerptText
        )
        {
            Calls.Add((fromLine, toLine, excerptText));
            return $"https://example.com/{fromLine}-{toLine}";
        }
    }

    private static string Render(
        string[] lines,
        List<int> matches,
        string keyword,
        IDocumentExcerptLinkBuilder linkBuilder
    )
    {
        var method = typeof(DocumentKeywordScan).GetMethod(
            "AppendMatchBlocks",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        var result = new StringBuilder();
        var document = new Document
        {
            CommonStock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 12, 31),
        };
        method!.Invoke(null, [result, lines, matches, keyword, document, linkBuilder]);
        return result.ToString();
    }

    private static readonly string[] Lines =
    [
        "alpha",
        "revenue grew strongly",
        "beta",
        "gamma",
        "delta",
        "epsilon",
        "costs of revenue fell",
        "zeta",
    ];

    [Fact]
    public void AppendMatchBlocks_TwoSeparateBlocks_EachEndsWithItsOwnLink()
    {
        var builder = new RecordingLinkBuilder();

        var rendered = Render(Lines, [1, 6], "revenue", builder);

        builder.Calls.Should().HaveCount(2);
        // Block one renders lines 1-3 (1-based): context, match, context.
        builder.Calls[0].Should().Be((1, 3, "revenue grew strongly"));
        // Block two renders lines 6-8.
        builder.Calls[1].Should().Be((6, 8, "costs of revenue fell"));
        rendered.Should().Contain("Link: https://example.com/1-3");
        rendered.Should().Contain("Link: https://example.com/6-8");
    }

    [Fact]
    public void AppendMatchBlocks_LinkNamesTheMatchedLine_NotAContextLine()
    {
        var builder = new RecordingLinkBuilder();

        Render(Lines, [1], "revenue", builder);

        builder.Calls.Should().ContainSingle();
        builder.Calls[0].ExcerptText.Should().Be("revenue grew strongly");
    }

    [Fact]
    public void AppendMatchBlocks_NoBuilder_RendersNoLinkLines()
    {
        var rendered = Render(Lines, [1, 6], "revenue", null);

        rendered.Should().NotContain("Link:");
    }
}
