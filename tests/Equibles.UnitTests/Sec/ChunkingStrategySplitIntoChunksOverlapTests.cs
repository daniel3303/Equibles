using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategySplitIntoChunksOverlapTests
{
    private readonly ChunkingStrategy _strategy = new(new TokenCounter());

    [Fact]
    public void SplitIntoChunks_ConsecutiveChunks_OverlapInCharacterRange()
    {
        // Contract (SplitIntoChunks: "Advance by chunk size minus overlap",
        // OverlapTokenSize = 128): an overlapping chunker exists so adjacent
        // chunks share context for RAG retrieval — so chunk N+1 must START before
        // chunk N ENDS. Existing pins assert count>1, first StartPosition=0, and
        // non-empty content, but never the overlap itself; a refactor advancing by
        // the full window (no overlap) would pass all of them yet break retrieval.
        var text = string.Join(
            " ",
            Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 300)
        );

        var result = _strategy.SplitIntoChunks(text);

        result.Should().HaveCountGreaterThan(1);
        result[1].StartPosition.Should().BeLessThan(result[0].EndPosition);
    }
}
