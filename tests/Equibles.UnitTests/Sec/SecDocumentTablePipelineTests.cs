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
        tableLines[4].Should().Be("| Compute & Networking | 193,479 | 116,193 | 77,286 | 67 | % |");
        tableLines[5].Should().Be("| Graphics | 22,459 | 14,304 | 8,155 | 57 | % |");
        tableLines[6].Should().Be("| Total | 215,938 | 130,497 | 85,441 | 65 | % |");

        var chunks = new ChunkingStrategy(new TokenCounter()).SplitIntoChunks(markdown);
        chunks.Should().ContainSingle();
        chunks[0].Content.Should().Contain("77,286 | 67 | % |\n| Graphics | 22,459");
    }
}
