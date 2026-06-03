using System.Reflection;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyFindLastSentenceEndNoBoundaryTests
{
    // Contract (ChunkingStrategy.FindLastSentenceEnd, ChunkingStrategy.cs:120): return the position
    // after the latest of ".", "!", "?", "\n\n" in [start,end), or the -1 sentinel when none exist.
    // The sibling pins all hit the found arm; the no-boundary -1 arm is unexercised. When a chunk's
    // overlap zone has no sentence ending, the caller must keep the full window — not truncate.
    [Fact]
    public void FindLastSentenceEnd_NoSentenceEndingInRange_ReturnsNegativeOne()
    {
        var strategy = new ChunkingStrategy(new TokenCounter());
        var method = typeof(ChunkingStrategy).GetMethod(
            "FindLastSentenceEnd",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        const string text = "no sentence ending here";
        var result = (int)method!.Invoke(strategy, [text, 0, text.Length]);

        result.Should().Be(-1);
    }
}
