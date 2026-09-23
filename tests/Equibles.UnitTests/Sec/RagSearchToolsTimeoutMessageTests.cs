using Equibles.Mcp;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Mcp.Tools;
using Equibles.Sec.Repositories;

namespace Equibles.UnitTests.Sec;

// A calling model acts on the timeout wording. A corpus-wide search has no bounded fallback, so it
// must be told to narrow the call; "retry the same call" repeated the failure (production,
// 2026-09-23). A scoped search already degraded, so a warm retry is still the advice there.
public class RagSearchToolsTimeoutMessageTests
{
    private static Task<List<Chunk>> TimesOut() =>
        throw new ChunkSearchTimeoutException("statement budget elapsed", new TimeoutException());

    [Fact]
    public async Task UnscopedTimeout_AsksForATickerOrAShorterQuery()
    {
        var fault = await Assert.ThrowsAsync<McpToolFaultException>(() =>
            RagSearchTools.SearchOrTimeoutFault(TimesOut, scoped: false)
        );

        Assert.Equal(RagSearchTools.UnscopedSearchTimedOutMessage, fault.Message);
        Assert.Contains("ticker", fault.Message);
        Assert.DoesNotContain("Retry the same call", fault.Message);
        Assert.IsType<ChunkSearchTimeoutException>(fault.InnerException);
    }

    [Fact]
    public async Task ScopedTimeout_KeepsTheRetryAdvice()
    {
        var fault = await Assert.ThrowsAsync<McpToolFaultException>(() =>
            RagSearchTools.SearchOrTimeoutFault(TimesOut, scoped: true)
        );

        Assert.Equal(RagSearchTools.ScopedSearchTimedOutMessage, fault.Message);
        Assert.Contains("Retry the same call", fault.Message);
    }

    [Fact]
    public async Task OtherFaults_EscapeUntouched()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RagSearchTools.SearchOrTimeoutFault(
                () => throw new InvalidOperationException("index corrupt"),
                scoped: false
            )
        );
    }
}
