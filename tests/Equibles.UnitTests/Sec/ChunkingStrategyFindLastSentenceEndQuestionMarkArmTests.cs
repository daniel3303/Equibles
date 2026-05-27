using System.Reflection;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyFindLastSentenceEndQuestionMarkArmTests
{
    // Contract (ChunkingStrategy.FindLastSentenceEnd, ChunkingStrategy.cs:120):
    //   var sentenceEndings = new[] { ".", "!", "?", "\n\n" };
    //   var lastIndex = sentenceEndings.Max(ending =>
    //       searchText.LastIndexOf(ending, StringComparison.Ordinal));
    //   return lastIndex > -1 ? start + lastIndex + 1 : -1;
    //
    // The contract: of the FOUR sentence-ending tokens (".", "!", "?",
    // "\n\n"), pick the one whose LastIndexOf returns the highest
    // position, and return the character index AFTER it. The Max-over-
    // sentence-endings is what gives the helper its "last sentence end"
    // semantics — it splits a chunk on the latest reasonable sentence
    // boundary in the overlap zone, not the first.
    //
    // Existing sibling pins:
    //   • ChunkingStrategyFindLastSentenceEndNegativeStartTests — pins
    //     the `start < 0 → start = 0` clamp via reflection.
    //   • The ChunkingStrategyTests integration-style pins exercise
    //     SplitIntoChunks via the public entry point but never
    //     reflection-invoke FindLastSentenceEnd with inputs that
    //     distinguish individual sentence-ending arms.
    //
    // The risks this pin uniquely catches and that are unreachable
    // from every existing sibling:
    //
    //   • Dropped "?" from the array — a "tidy up — questions are
    //     rare in SEC filings" cleanup. The Max would then be driven
    //     by the EARLIER period (or no ending at all if the input
    //     has only a question mark), silently producing a shorter
    //     chunk that splits on the previous period instead of the
    //     question mark at the chunk boundary. Every SEC filing's
    //     management-discussion section opens question-style
    //     subheaders ("How did we perform?") — those would now
    //     chunk-break one sentence earlier than intended.
    //
    //   • Swap Max → Min — a "simplify" refactor that picked the
    //     FIRST sentence ending instead of the last. The existing
    //     sibling pin (negative start clamp) doesn't exercise the
    //     Max/Min selection. With multiple endings in the search
    //     range, a Min would silently shrink every chunk to the
    //     first ending in the overlap zone, doubling chunk count
    //     for long sections.
    //
    //   • Drop "+1" — `return start + lastIndex` instead of
    //     `start + lastIndex + 1`. The contract is "position AFTER
    //     the sentence ending" so the chunk includes the punctuation.
    //     A dropped +1 would split BEFORE the punctuation, leaving
    //     orphan question marks at the head of every subsequent
    //     chunk.
    //
    // Pin: feed a text where "?" is the LAST sentence-ending character
    // and "." is also present at an earlier position. The contract:
    //   text = "First. Second?"  (length 14)
    //   "." position = 5; "?" position = 13
    //   max = 13; return start + 13 + 1 = 14.
    //
    // Asserting exactly 14 (not 6, which would be "First. " — the
    // result if "?" were dropped) distinguishes:
    //   • Working contract → 14
    //   • Dropped "?" arm → 6 (uses "." position)
    //   • Swap to Min → 6
    //   • Drop +1 → 13 (off-by-one at the punctuation)
    //
    // Reflection-invoke since the helper is private instance.
    // Use a real ChunkingStrategy with the real tokenizer — the
    // method doesn't touch the tokenizer in its body, but the
    // private-instance signature requires an instance.
    [Fact]
    public void FindLastSentenceEnd_QuestionMarkAtEndAfterEarlierPeriod_ReturnsPositionAfterQuestionMark()
    {
        var strategy = new ChunkingStrategy(new TokenCounter());
        var method = typeof(ChunkingStrategy).GetMethod(
            "FindLastSentenceEnd",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var result = (int)method!.Invoke(strategy, ["First. Second?", 0, 14]);

        result.Should().Be(14);
    }
}
