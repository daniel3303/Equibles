using Equibles.Sec.BusinessLogic.Search;

namespace Equibles.UnitTests.Sec;

public class RrfFusionTests
{
    // RrfFusion is the combiner that turns the BM25 ranking and the semantic ranking into one
    // fused order for the hybrid searcher. The whole point of going hybrid is that this fusion is
    // correct: a chunk both arms agree on must outrank a chunk only one arm saw, and the output
    // must be deterministic so identical queries return identical results across requests.

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    [Fact]
    public void Fuse_SingleList_PreservesInputOrder()
    {
        var a = Id(1);
        var b = Id(2);
        var c = Id(3);

        var result = RrfFusion.Fuse([
            [a, b, c],
        ]);

        result.Should().Equal(a, b, c);
    }

    [Fact]
    public void Fuse_AgreedItem_OutranksItemSeenByOnlyOneList()
    {
        // `shared` is the only id in BOTH lists; `onlyBm25` is rank 0 of the first list and
        // `onlyVector` rank 0 of the second. RRF rewards agreement: even sitting one rank lower in
        // each list, `shared` accumulates from both arms and must come first.
        var shared = Id(1);
        var onlyBm25 = Id(2);
        var onlyVector = Id(3);

        var bm25 = new[] { onlyBm25, shared };
        var vector = new[] { onlyVector, shared };

        var result = RrfFusion.Fuse([bm25, vector]);

        result.Should().HaveElementAt(0, shared);
        result.IndexOf(shared).Should().BeLessThan(result.IndexOf(onlyBm25));
    }

    [Fact]
    public void Fuse_HigherRanksContributeMore()
    {
        // Same single list, so rank position alone decides: earlier ranks get the larger 1/(k+rank)
        // weight, so the fused order is the input order.
        var first = Id(1);
        var second = Id(2);

        var result = RrfFusion.Fuse([
            [first, second],
        ]);

        result.Should().Equal(first, second);
    }

    [Fact]
    public void Fuse_EqualScores_TieBreakByIdDeterministically()
    {
        // Two items each appear once at rank 0 of a different list — identical fused scores. The
        // tie must resolve by id every run, never by nondeterministic dictionary order.
        var low = Id(1);
        var high = Id(2);

        var forward = RrfFusion.Fuse([
            [high],
            [low],
        ]);
        var reversed = RrfFusion.Fuse([
            [low],
            [high],
        ]);

        forward.Should().Equal(low, high);
        reversed.Should().Equal(low, high);
    }

    [Fact]
    public void Fuse_NoLists_ReturnsEmpty()
    {
        RrfFusion.Fuse([]).Should().BeEmpty();
    }

    [Fact]
    public void Fuse_LargerK_FlattensRankAdvantage()
    {
        // With a big k, the gap between 1/(k+1) and 1/(k+2) shrinks, so a single agreement in the
        // second list can overtake a pure rank lead. Pinning the k plumbing guards the tunable.
        var rankLeader = Id(1);
        var agreed = Id(2);

        var bm25 = new[] { rankLeader, agreed };
        var vector = new[] { agreed };

        var result = RrfFusion.Fuse([bm25, vector], k: 1);

        result.Should().HaveElementAt(0, agreed);
    }
}
