using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.UnitTests.Sec;

public class RagManagerBuildContextStartLineNumberTests
{
    [Fact]
    public async Task BuildContext_ZeroStartLineNumber_OmitsLineHintFromExcerptHeader()
    {
        // RagManager.BuildContext renders each chunk's excerpt header conditionally on
        // chunk.StartLineNumber > 0:
        //   true  -> "**Excerpt {N} (line ~{StartLineNumber:N0}):**"
        //   false -> "**Excerpt {N}:**"
        //
        // StartLineNumber is `int` (default 0) on Chunk — it's populated by the chunking
        // pipeline only when the source document carries usable line-offset metadata.
        // PDF / HTML chunkers without reliable line indexing leave it at 0. The implicit
        // contract a caller relies on: "0" is the sentinel for unknown, and the header
        // must drop the parenthetical hint entirely — never render `(line ~0)`, which
        // would lead an LLM to either cite "line 0" verbatim (false attribution) or
        // dismiss the excerpt's origin (low confidence). The just-fixed culture pin
        // (GH-2608) only covers the `> 0` arm; this is the companion `<= 0` arm.
        //
        // The risk this test pins: a "tidy-up" refactor that flips `> 0` to `>= 0`,
        // drops the conditional altogether, or replaces StartLineNumber with
        // (StartLineNumber ?? 0) on a future schema change — all compile clean and
        // silently emit `**Excerpt 1 (line ~0):**` for the entire PDF/HTML corpus.
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
                StartLineNumber = 0,
                Content = "some excerpt text",
                Document = document,
            },
        };

        var sut = new RagManager(hybridChunkSearcher: null, commonStockRepository: null, logger: null);

        var output = await sut.BuildContext(chunks);

        output
            .Should()
            .Contain(
                "**Excerpt 1:**",
                "StartLineNumber=0 means line-offset metadata was unavailable for this chunk; the header must drop the `(line ~N)` parenthetical rather than render `(line ~0)`"
            )
            .And.NotContain(
                "(line ~",
                "no `(line ~...)` hint should appear in the rendered context when StartLineNumber is non-positive — emitting one would either misattribute the excerpt to line 0 or expose the sentinel to the LLM prompt"
            );
    }
}
