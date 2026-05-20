using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.UnitTests.Sec;

public class RagManagerBuildContextWhitespaceChunkTests
{
    [Fact]
    public async Task BuildContext_WhitespaceOnlyChunkContent_OmitsExcerptHeader()
    {
        // RagManager.BuildContext feeds its rendered string straight into the LLM
        // context window. Chunks whose Content is whitespace-only carry zero
        // information value — the embedding lookup is fuzzy and routinely returns
        // boilerplate spacer chunks (table cell padding, page-break artifacts,
        // PDF column gutters). The implicit contract a caller relies on is that
        // such chunks must be SILENTLY DROPPED from the rendered output, not
        // emitted as an empty `**Excerpt N:**\n\n` block.
        //
        // The risk: a refactor that "simplifies" the rendering loop by removing
        // the `if (!string.IsNullOrWhiteSpace(chunk.Content))` guard — say,
        // because the IsNullOrWhiteSpace check looks redundant to a reader who
        // assumes HybridSearch has already filtered junk chunks — would compile,
        // pass both existing pins (StartPosition ordering, empty-list early
        // exit), and pollute the LLM context with header-only excerpts. The LLM
        // either spends tokens on the empty headers (best case) or hallucinates
        // content to fit the headers (worst case). Either failure mode is
        // invisible from existing CI: the assembled string still contains the
        // expected document group, and the test suite doesn't measure context
        // quality.
        //
        // Pin: a single chunk with all-whitespace Content must NOT produce an
        // `**Excerpt 1` header in the output. The group's document title still
        // renders (that's a separate code path), so the absence of the excerpt
        // header is a precise signal that the skip arm fired.
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var document = new Document
        {
            CommonStock = stock,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2024, 12, 31),
        };
        var chunks = new List<Chunk>
        {
            new()
            {
                Index = 0,
                StartPosition = 100,
                StartLineNumber = 10,
                Content = "   \t  \n  ",
                Document = document,
            },
        };

        var sut = new RagManager(chunkRepository: null, commonStockRepository: null, logger: null);

        var result = await sut.BuildContext(chunks);

        result.Should().NotContain("**Excerpt 1");
    }
}
