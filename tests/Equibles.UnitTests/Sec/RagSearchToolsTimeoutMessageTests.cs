using Equibles.Mcp;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;

namespace Equibles.UnitTests.Sec;

// A calling model acts on the timeout wording. A corpus-wide search has no bounded fallback, so it
// must be told to narrow the call; "retry the same call" repeated the failure (production,
// 2026-09-23). A scoped search already degraded, so a warm retry is still the advice there.
// Shortening is advised only when the query has more terms than the shortened-query pass keeps.
public class RagSearchToolsTimeoutMessageTests
{
    private const string ShortQuery = "segment revenue";
    private const string LongQuery = "Alberta Energy Regulator mandatory closure spend 2026";

    private static Task<List<Chunk>> TimesOut() =>
        throw new ChunkSearchTimeoutException("statement budget elapsed", new TimeoutException());

    [Fact]
    public async Task UnscopedTimeout_ShortQuery_AsksForATickerOrADateWindow_NotAShorterQuery()
    {
        var fault = await Assert.ThrowsAsync<McpToolFaultException>(() =>
            RagSearchTools.SearchOrTimeoutFault(TimesOut, scoped: false, query: ShortQuery)
        );

        Assert.Equal(SearchTimeoutAdvice.Unscoped, fault.Message);
        Assert.Contains("ticker", fault.Message);
        Assert.Contains("date window", fault.Message);
        Assert.DoesNotContain("shorten", fault.Message);
        Assert.DoesNotContain("Retry the same call", fault.Message);
        Assert.IsType<ChunkSearchTimeoutException>(fault.InnerException);
        Assert.Equal(McpToolErrorReason.Timeout, fault.Reason);
    }

    [Fact]
    public async Task UnscopedTimeout_LongQuery_AsksForATickerOrAShorterQuery()
    {
        var fault = await Assert.ThrowsAsync<McpToolFaultException>(() =>
            RagSearchTools.SearchOrTimeoutFault(TimesOut, scoped: false, query: LongQuery)
        );

        Assert.Equal(SearchTimeoutAdvice.UnscopedLongQuery, fault.Message);
        Assert.Contains("ticker", fault.Message);
        Assert.Contains("shorten the query", fault.Message);
        Assert.DoesNotContain("Retry the same call", fault.Message);
    }

    [Fact]
    public async Task ScopedTimeout_ShortQuery_KeepsTheRetryAdvice_AndNarrowsInsteadOfShortening()
    {
        var fault = await Assert.ThrowsAsync<McpToolFaultException>(() =>
            RagSearchTools.SearchOrTimeoutFault(TimesOut, scoped: true, query: ShortQuery)
        );

        Assert.Equal(SearchTimeoutAdvice.Scoped, fault.Message);
        Assert.Contains("Retry the same call", fault.Message);
        Assert.Contains("date window", fault.Message);
        Assert.DoesNotContain("shorten", fault.Message);
    }

    [Fact]
    public async Task ScopedTimeout_LongQuery_KeepsTheRetryAdvice_AndOffersAShorterQuery()
    {
        var fault = await Assert.ThrowsAsync<McpToolFaultException>(() =>
            RagSearchTools.SearchOrTimeoutFault(TimesOut, scoped: true, query: LongQuery)
        );

        Assert.Equal(SearchTimeoutAdvice.ScopedLongQuery, fault.Message);
        Assert.Contains("Retry the same call", fault.Message);
        Assert.Contains("shorten the query", fault.Message);
    }

    // Shortening a phrase searches for a different phrase, so phrase advice only narrows scope.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhraseTimeout_NarrowsTheScopeWithoutRewritingThePhrase(bool scoped)
    {
        var fault = await Assert.ThrowsAsync<McpToolFaultException>(() =>
            RagSearchTools.SearchOrTimeoutFault(TimesOut, scoped, SearchMatch.Phrase, LongQuery)
        );

        Assert.Equal(
            scoped ? SearchTimeoutAdvice.ScopedPhrase : SearchTimeoutAdvice.UnscopedPhrase,
            fault.Message
        );
        Assert.DoesNotContain("shorten the query", fault.Message);
    }

    [Fact]
    public async Task OtherFaults_EscapeUntouched()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RagSearchTools.SearchOrTimeoutFault<List<Chunk>>(
                () => throw new InvalidOperationException("index corrupt"),
                scoped: false
            )
        );
    }

    // An incomplete search stopped early, so "only N matched" would be false; it says to retry.
    [Fact]
    public void ShortfallNote_IncompleteSearch_SaysItRanOutOfTime_NotThatOnlyNMatched()
    {
        var note = RagSearchTools.AppendShortfallNote("excerpts\n", 2, 5, incomplete: true);

        Assert.EndsWith(RagSearchTools.IncompleteNote, note);
        Assert.Contains("ran out of time", note);
        Assert.Contains("Retry the same call", note);
        Assert.DoesNotContain("Only 2", note);
    }

    [Fact]
    public void ShortfallNote_CompleteSearch_KeepsTheOnlyNMatchedNote()
    {
        var note = RagSearchTools.AppendShortfallNote("excerpts\n", 2, 5);

        Assert.Contains("Only 2 excerpt(s) matched", note);
        Assert.DoesNotContain("ran out of time", note);
    }
}
