using Equibles.Sec.BusinessLogic;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class SecDocumentTablePipelineTests
{
    [Fact]
    public void NormalizeAndConvert_NvidiaSegmentTable_PreservesRowsAndPeriodColumns()
    {
        var submission = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Sec",
                "nvda-2026-segment-table.html"
            )
        );

        var normalized = new SecDocumentHtmlNormalizer().Normalize(submission);
        var markdown = new SecDocumentHtmlToMarkdownConverter().Convert(normalized);
        var tableLines = markdown.Split('\n').Where(line => line.StartsWith('|')).ToList();

        tableLines.Should().HaveCount(7);
        tableLines[2].Should().Contain("Jan 25, 2026").And.Contain("Jan 26, 2025");
        tableLines[4].Should().Be("| Compute & Networking | 193,479 | 116,193 | 77,286 | 67% |");
        tableLines[5].Should().Be("| Graphics | 22,459 | 14,304 | 8,155 | 57% |");
        tableLines[6].Should().Be("| Total | 215,938 | 130,497 | 85,441 | 65% |");

        var chunks = new ChunkingStrategy(new TokenCounter()).SplitIntoChunks(markdown);
        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Contain("77,286 | 67% |\n| Graphics | 22,459");
    }

    // A statement as one filer's software writes it: every spacer cell holds a zero-width space, so
    // the spacer rows and columns, the "$" columns and the years one column off all used to survive.
    [Fact]
    public void NormalizeAndConvert_StatementPaddedWithZeroWidthSpaces_ReadsAsAPlainTable()
    {
        const string zws = "<p><span>\u200B</span></p>";
        var html =
            "<table><tbody>"
            + $"<tr><td>{zws}</td><td>{zws}</td><td colspan=\"5\"><p><b>Three Months Ended September&nbsp;30,&nbsp;</b></p></td></tr>"
            + $"<tr><td>{zws}</td><td><p>&nbsp;&nbsp;\u200B&nbsp;\u200B</p></td><td colspan=\"2\"><p><b>2026</b></p></td><td><p>&nbsp;&nbsp;\u200B&nbsp;\u200B</p></td><td colspan=\"2\"><p><b>2025</b></p></td></tr>"
            + $"<tr><td><p>Revenues</p></td><td>{zws}</td><td><p>$</p></td><td><p> 2,638,081</p></td><td>{zws}</td><td><p>$</p></td><td><p> 2,458,378</p></td></tr>"
            + $"<tr><td><p>Cost of operations</p></td><td>{zws}</td><td>{zws}</td><td><p> 1,501,143</p></td><td>{zws}</td><td>{zws}</td><td><p> 1,406,854</p></td></tr>"
            + "</tbody></table>";

        var markdown = new SecDocumentHtmlToMarkdownConverter().Convert(
            new SecDocumentHtmlNormalizer().NormalizeFragment(html)
        );

        markdown
            .Split('\n')
            .Where(line => line.StartsWith('|'))
            .Should()
            .Equal(
                "|  | Three Months Ended September 30, |  |",
                "| --- | --- | --- |",
                "|  | 2026 | 2025 |",
                "| Revenues | 2,638,081 | 2,458,378 |",
                "| Cost of operations | 1,501,143 | 1,406,854 |"
            );
    }

    [Fact]
    public void NormalizeAndConvert_SourceLineWrapsAndLinkTargets_LeaveNoBreaksOrEmptyLinks()
    {
        var html =
            "<p><a name=\"CASH\"></a>Statements of cash flows</p>"
            + "<table><tr><td>Cash\nFlows From Operating Activities:</td><td>(5,455,432)</td></tr>"
            + "<tr><td>Cost<br>of sales</td><td>567</td></tr></table>";

        var markdown = new SecDocumentHtmlToMarkdownConverter().Convert(
            new SecDocumentHtmlNormalizer().NormalizeFragment(html)
        );

        markdown.Should().NotContain("[]()");
        markdown.Should().Contain("| Cash Flows From Operating Activities: | (5,455,432) |");
        markdown.Should().Contain("| Cost<br>of sales | 567 |");
    }
}
