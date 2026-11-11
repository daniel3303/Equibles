using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

// A combined 8-K filed by a holding company and five subsidiaries is stored once per
// co-registrant. A market-wide search returned its earnings passage six times, one per ticker,
// and nothing else fitted in the result set.
public class HybridChunkSearcherCombinedFilingTests
{
    private static readonly DateTime Filed = new(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly string[] CoRegistrants = ["ELC", "EMP", "ENJ", "ETI-P", "EAI", "ETR"];

    // Six passages of six copies each are thirty-six rows: a pool a few times the request holds
    // five of them, and the sixth passage never arrives.
    [Fact]
    public async Task MarketWideSearch_ReturnsEachPassageOfACombinedFilingOnce_AndStillFillsTheResult()
    {
        var passages = Enumerable
            .Range(0, 6)
            .Select(index =>
                CoRegistrants
                    .Select(ticker =>
                        Passage(ticker, 100 + index, $"third quarter earnings, part {index}")
                    )
                    .ToList()
            )
            .ToList();
        var repository = new RankedChunkRepository(passages.SelectMany(copies => copies).ToList());

        var results = await NewSearcher(repository).Search("third quarter earnings", 6);

        Assert.Equal(passages.Select(copies => copies[0].Id), results.Select(chunk => chunk.Id));
    }

    // The any-word top-up asks whether the first pass filled the request; six copies of one
    // passage are one answer, not six.
    [Fact]
    public async Task MarketWideSearch_CountsCopiesOnce_WhenDecidingToBroadenTheQuery()
    {
        var copies = Copies("third quarter earnings of $2.28 per share");
        var broader = Passage("DUK", 40, "earnings rose in the third quarter");
        var repository = new RankedChunkRepository(copies, anyWord: [broader]);

        var results = await NewSearcher(repository)
            .Search("third quarter earnings per share", 2, disjunctiveFallback: true);

        Assert.Equal([copies[0].Id, broader.Id], results.Select(chunk => chunk.Id));
    }

    // The cap turned away the holding company's copy; a subsidiary's copy still carries the passage.
    [Fact]
    public async Task PerCompanyCap_KeepsAPassageWhoseBestCopyBelongsToACappedCompany()
    {
        var first = Passage("ETR", 12, "the board declared a quarterly dividend");
        var capped = Passage("ETR", 183, "third quarter earnings of $2.28 per share");
        var kept = Passage("ELC", 183, "third quarter earnings of $2.28 per share");
        var repository = new RankedChunkRepository([first, capped, kept]);

        var results = await NewSearcher(repository)
            .Search("third quarter earnings per share", 3, maxResultsPerCompany: 1);

        Assert.Equal([first.Id, kept.Id], results.Select(chunk => chunk.Id));
    }

    private static List<Chunk> Copies(string content) =>
        CoRegistrants.Select(ticker => Passage(ticker, 183, content)).ToList();

    private static Chunk Passage(string ticker, int line, string content) =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            DocumentId = Guid.NewGuid(),
            DocumentType = DocumentType.EightK,
            ReportingDate = Filed,
            StartLineNumber = line,
            Content = content,
        };

    private static HybridChunkSearcher NewSearcher(ChunkRepository repository)
    {
        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient.IsEnabled.Returns(false);
        return new HybridChunkSearcher(
            repository,
            new StubEmbeddingRepository(similarChunkIds: []),
            embeddingClient,
            Options.Create(new HybridSearchOptions()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            NullLogger<HybridChunkSearcher>.Instance
        );
    }

    // Returns the ranked hits up to the limit it is handed, as the index does: a pool only as
    // deep as the request would hold nothing but copies.
    private sealed class RankedChunkRepository(List<Chunk> ranked, List<Chunk> anyWord = null)
        : ChunkRepository(null)
    {
        public override Task<List<Chunk>> HybridSearch(
            string searchText,
            int maxResults,
            string ticker = null,
            IReadOnlyCollection<string> excludeTickers = null,
            Guid? documentId = null,
            IReadOnlyCollection<DocumentType> documentTypes = null,
            DateOnly? startDate = null,
            DateOnly? endDate = null,
            bool conjunctive = true,
            int? commandTimeoutSeconds = null,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                (conjunctive ? ranked : [.. ranked, .. anyWord ?? []]).Take(maxResults).ToList()
            );
    }
}
