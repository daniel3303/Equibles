using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyCleanTextBlockBoundaryTests
{
    private readonly ChunkingStrategy _strategy = new(new TokenCounter());

    // Contract (CleanText, ChunkingStrategy.cs:78-106): strip residual HTML so the
    // result is the *readable* document text embedded into RAG / public search.
    // Adjacent block-level cells in residual table HTML — the dominant structure in
    // SEC filings — must keep their boundary: a row label and its figure are distinct
    // tokens. AngleSharp's TextContent concatenates descendant text with NO separator,
    // so "<td>Net income</td><td>1000</td>" collapses to "Net income1000", gluing the
    // value onto the label and producing a junk token that pollutes embeddings/search.
    // A reader (and a searcher) relies on the two cells staying separate.
    [Fact(
        Skip = "GH-3842 — CleanText concatenates adjacent block/table-cell text, gluing label onto value"
    )]
    public void CleanText_AdjacentTableCells_DoesNotConcatenateLabelAndValue()
    {
        var result = _strategy.CleanText(
            "<table><tr><td>Net income</td><td>1000</td></tr></table>"
        );

        result.Should().Be("Net income 1000");
    }
}
