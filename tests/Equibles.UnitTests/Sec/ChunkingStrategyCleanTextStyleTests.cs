using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyCleanTextStyleTests
{
    private readonly ChunkingStrategy _strategy = new(new TokenCounter());

    // Sibling to CleanText_ScriptElement_DoesNotLeakScriptSourceIntoOutput.
    // The production code removes script AND style nodes via a comma-separated
    // CSS selector ("script, style") — but CSS selectors are split into two
    // independent compound selectors and a single test only exercises one. The
    // existing pin covers `<script>`; the `<style>` arm is unpinned. SEC EDGAR
    // filings routinely embed inline CSS rule bodies, and AngleSharp's
    // TextContent concatenates raw-text content from style elements just as it
    // does for script — left in, the CSS rule text would pollute embeddings
    // and RAG search results. A refactor that simplified the selector to just
    // "script" (or that called QuerySelectorAll twice but missed updating the
    // second arm) would silently leak every style block into RAG.
    [Fact]
    public void CleanText_StyleElement_DoesNotLeakCssRuleBodyIntoOutput()
    {
        var result = _strategy.CleanText(
            "<style>.report-header { color: red; content: 'pollute-rag'; }</style>"
                + "<p>Real annual report content.</p>"
        );

        result.Should().Be("Real annual report content.");
    }
}
