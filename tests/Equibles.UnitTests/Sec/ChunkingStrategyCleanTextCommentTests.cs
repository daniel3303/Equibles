using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyCleanTextCommentTests
{
    private readonly ChunkingStrategy _strategy = new(new TokenCounter());

    // Contract (CleanText, ChunkingStrategy.cs:89-100): TextContent concatenates
    // the text of ALL descendants including comments, so comment nodes are
    // explicitly removed — their source must not leak into the cleaned text that
    // gets embedded into RAG / public search. The sibling test pins script/style
    // removal; the comment-removal branch is unpinned. A refactor dropping the
    // comment.Remove() loop would pass every existing test yet leak filing
    // boilerplate / internal notes into searchable content.
    [Fact]
    public void CleanText_HtmlComment_DoesNotLeakCommentTextIntoOutput()
    {
        var result = _strategy.CleanText(
            "<!-- internal reviewer note: do not publish -->" + "<p>Real annual report content.</p>"
        );

        result.Should().Be("Real annual report content.");
    }
}
