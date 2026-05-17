using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.BusinessLogic.Tokenization;

namespace Equibles.UnitTests.Sec;

public class ChunkingStrategyCleanTextScriptTests
{
    private readonly ChunkingStrategy _strategy = new(new TokenCounter());

    // Contract: CleanText strips residual HTML so the result is the readable
    // document text that gets embedded into RAG / public search. A caller
    // relies on script source NOT leaking through — minified JS in a filing's
    // boilerplate must not become searchable "content".
    [Fact]
    public void CleanText_ScriptElement_DoesNotLeakScriptSourceIntoOutput()
    {
        var result = _strategy.CleanText(
            "<script>alert('xss-leak')</script><p>Real annual report content.</p>"
        );

        result.Should().Be("Real annual report content.");
    }
}
