using System.Reflection;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyFindLastSentenceEndNegativeStartTests
{
    // FindLastSentenceEnd is called from the chunking pipeline with `start =
    // tokenOffset - OverlapTokenSize`, which can go negative when the token
    // offset hasn't yet exceeded the overlap window (early chunks of a short
    // document). The leading `if (start < 0) start = 0;` guard is the load-
    // bearing safety — `text.Substring(start, end - start)` would otherwise
    // throw ArgumentOutOfRangeException on the first chunk of every short
    // document and abort the SEC text-index ingestion. A refactor that
    // dropped the guard ("the caller already passed a positive start") would
    // compile cleanly and silently kill chunking for any document shorter
    // than the overlap-token window.
    [Fact]
    public void FindLastSentenceEnd_NegativeStart_ClampsToZeroWithoutThrowing()
    {
        var strategy = new ChunkingStrategy(new TokenCounter());
        var method = typeof(ChunkingStrategy).GetMethod(
            "FindLastSentenceEnd",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        int result = -999;
        var act = () => result = (int)method.Invoke(strategy, ["Hello.", -1, 6]);

        act.Should().NotThrow();
        result.Should().Be(6);
    }
}
