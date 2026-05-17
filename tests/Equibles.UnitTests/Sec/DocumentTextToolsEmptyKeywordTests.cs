using System.Reflection;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class DocumentTextToolsEmptyKeywordTests
{
    private static readonly MethodInfo HighlightKeywordMethod = typeof(DocumentTextTools).GetMethod(
        "HighlightKeyword",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // Contract: HighlightKeyword wraps every occurrence of `keyword` in a line.
    // An empty keyword has no meaningful occurrence, so the only safe behaviour
    // a caller can rely on is termination returning the line unchanged. The
    // public SearchDocumentKeyword MCP tool passes its unvalidated keyword arg
    // straight here, so an empty keyword is reachable and must not hang.
    // (Ambiguity: empty keyword could be argued a caller error — but it must
    // never produce an unbounded loop / DoS.) Timeout-bounded so the harness
    // can't hang; exceeding it is the failure signal.
    [Fact(Timeout = 2000)]
    public async Task HighlightKeyword_EmptyKeyword_ReturnsLineUnchangedWithoutHanging()
    {
        var result = await Task.Run(() => (string)HighlightKeywordMethod.Invoke(null, ["abc", ""]));

        result.Should().Be("abc");
    }
}
