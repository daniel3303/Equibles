using System.Reflection;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyFindLastSentenceEndParagraphBreakArmTests
{
    // Contract (ChunkingStrategy.FindLastSentenceEnd, ChunkingStrategy.cs:120):
    //   var sentenceEndings = new[] { ".", "!", "?", "\n\n" };
    //   var lastIndex = sentenceEndings.Max(ending =>
    //       searchText.LastIndexOf(ending, StringComparison.Ordinal));
    //   return lastIndex > -1 ? start + lastIndex + 1 : -1;
    //
    // Existing sibling pins exercise ".", "?", and the negative-start clamp.
    // The fourth sentence-ending token, "\n\n", is the paragraph-break arm —
    // unique among the four because it's the only multi-character ending.
    // Without this arm SEC filings whose chunk-overlap zone ends on a clean
    // paragraph break (and not a period/!/?) would lose their natural
    // break point and the chunker would fall back to mid-paragraph cuts,
    // hurting embedding quality.
    //
    // The risks this pin uniquely catches and that are unreachable from
    // every existing sibling:
    //
    //   • Dropped "\n\n" from the array — a "regex-like cleanup" that
    //     removes the non-punctuation token. With no other ending in
    //     the input, the helper would return -1 (no sentence end found)
    //     and SplitIntoChunks would fall back to the raw token window
    //     boundary, breaking mid-paragraph.
    //
    //   • Swap Max → Min — already pinned for the punctuation arms, but
    //     this input lets the regression manifest with no period present
    //     at all (no false-positive from the period arm masking it).
    //
    //   • Drop "+1" — `start + lastIndex` instead of `start + lastIndex + 1`.
    //     Returns 5 (position of the first \n) instead of 6 (position
    //     after the first \n). Existing siblings catch the same drop for
    //     "?"; this catches it for the paragraph-break arm specifically.
    //
    // Pin: feed a text whose ONLY sentence ending is "\n\n", so the result
    // is driven entirely by the paragraph-break arm.
    //   text = "First\n\n"  (length 7)
    //   ".", "!", "?" LastIndexOf → -1, -1, -1
    //   "\n\n" LastIndexOf = 5
    //   max = 5; return start + 5 + 1 = 6.
    //
    // Reflection-invoke since the helper is private instance. Use a real
    // ChunkingStrategy with the real tokenizer — the method doesn't touch
    // the tokenizer, but the private-instance signature requires an instance.
    [Fact]
    public void FindLastSentenceEnd_OnlyParagraphBreak_ReturnsLastIndexPlusOne()
    {
        var strategy = new ChunkingStrategy(new TokenCounter());
        var method = typeof(ChunkingStrategy).GetMethod(
            "FindLastSentenceEnd",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var result = (int)method!.Invoke(strategy, ["First\n\n", 0, 7]);

        result.Should().Be(6);
    }
}
