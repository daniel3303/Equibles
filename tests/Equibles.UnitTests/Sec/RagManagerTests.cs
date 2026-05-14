using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;

namespace Equibles.UnitTests.Sec;

public class RagManagerTests
{
    [Fact]
    public async Task BuildContext_ChunksFromSameDocumentOutOfOrder_OutputsByStartPositionAscending()
    {
        // RagManager.BuildContext groups chunks per (Ticker, DocumentType, ReportingDate) and
        // emits each group's excerpts ordered by Chunk.StartPosition. The order matters: the
        // assembled string is fed to an LLM, and disordered excerpts produce non-sequential
        // narrative — e.g. the Risk Factors discussion appearing AFTER its dependent forward-
        // looking-statements section. HybridSearch returns chunks ranked by relevance, NOT
        // by position, so this OrderBy is the only thing keeping output sequential.
        //
        // The risk this test pins: a "tidy-up" refactor that drops the OrderBy (or replaces
        // it with .OrderBy(c => c.Index)) would silently scramble every multi-chunk RAG
        // response. Index and StartPosition are usually monotonic together — but Index can
        // be reassigned during chunk-table backfills, while StartPosition is the source of
        // truth from the original document offsets. Pinning StartPosition specifically
        // catches the OrderBy → OrderBy(Index) substitution.
        //
        // Construction: three chunks from the same document, fed in deliberately wrong order
        // (StartPositions 500, 100, 300 in that sequence). Output should be "Excerpt 1 (line ~10)",
        // "Excerpt 3 (line ~30)", "Excerpt 2 (line ~50)" — but importantly the *content* must
        // appear at positions matching StartPosition ascending: "FIRST" → "SECOND" → "THIRD".
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
                StartPosition = 500,
                StartLineNumber = 50,
                Content = "THIRD excerpt",
                Document = document,
            },
            new()
            {
                Index = 1,
                StartPosition = 100,
                StartLineNumber = 10,
                Content = "FIRST excerpt",
                Document = document,
            },
            new()
            {
                Index = 2,
                StartPosition = 300,
                StartLineNumber = 30,
                Content = "SECOND excerpt",
                Document = document,
            },
        };

        var sut = new RagManager(chunkRepository: null, commonStockRepository: null, logger: null);

        var result = await sut.BuildContext(chunks);

        var firstIdx = result.IndexOf("FIRST", StringComparison.Ordinal);
        var secondIdx = result.IndexOf("SECOND", StringComparison.Ordinal);
        var thirdIdx = result.IndexOf("THIRD", StringComparison.Ordinal);

        firstIdx.Should().BePositive();
        secondIdx.Should().BeGreaterThan(firstIdx);
        thirdIdx.Should().BeGreaterThan(secondIdx);
    }

    [Fact]
    public async Task BuildContext_EmptyChunkList_ReturnsNoDocumentsFoundMessage()
    {
        // Sibling to the StartPosition-ordering pin above. The risk this catches is
        // asymmetric and unreachable from the existing test: `BuildContext` begins
        // with an early guard
        //   if (!chunks.Any()) return Task.FromResult("No relevant financial documents found.");
        // EVERY downstream line (the GroupBy on `c.Document.CommonStock.Ticker`, the
        // `group.First()` enumerator, the `chunks.OrderBy` inside the inner foreach)
        // would NRE on an empty list because Document navigation is fetched lazily —
        // not because of empty enumeration itself, but because the very first chunk
        // touch in `groupedChunks.First()` would crash if the guard were dropped.
        //
        // The user-visible failure mode is a generic "An error occurred while
        // searching the document. Please try again." message from McpToolExecutor's
        // outer catch, with no signal to the operator about WHY the empty-results
        // case suddenly started erroring. The friendly "No relevant financial
        // documents found." string is the contract the LLM consumer relies on to
        // distinguish "we searched and found nothing" from "the search itself
        // failed". A regression here corrupts that distinction silently — every
        // legitimate zero-result query starts looking like a server failure, and
        // the LLM either retries (wasting tokens) or surfaces a misleading error
        // to the user.
        //
        // The pair (non-empty → ordered excerpts, empty → friendly empty-message)
        // distinguishes a working guard from BOTH guard-dropped (NRE catchpath)
        // AND guard-inverted (wrong message for non-empty case) regressions.
        var sut = new RagManager(chunkRepository: null, commonStockRepository: null, logger: null);

        var result = await sut.BuildContext(new List<Chunk>());

        result.Should().Be("No relevant financial documents found.");
    }
}
