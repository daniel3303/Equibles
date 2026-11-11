using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Search;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
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

    [Fact]
    public async Task MarketWideSearch_ReturnsAPassageOfACombinedFilingOnce()
    {
        var copies = new[] { "ELC", "EMP", "ENJ", "ETI-P", "EAI", "ETR" }
            .Select(ticker => Passage(ticker, 183, "third quarter earnings of $2.28 per share"))
            .ToList();
        var nextPassage = Passage("EAI", 231, "the company affirmed its guidance range");
        var otherCompany = Passage("DUK", 183, "third quarter earnings of $1.81 per share");
        var searcher = NewSearcher([.. copies, nextPassage, otherCompany]);

        var results = await searcher.Search("third quarter earnings per share", 6);

        Assert.Equal(
            [copies[0].Id, nextPassage.Id, otherCompany.Id],
            results.Select(chunk => chunk.Id)
        );
    }

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

    private static HybridChunkSearcher NewSearcher(List<Chunk> ranked)
    {
        var embeddingClient = Substitute.For<IEmbeddingClient>();
        embeddingClient.IsEnabled.Returns(false);
        return new HybridChunkSearcher(
            new StubChunkRepository(bm25Results: ranked, allChunks: ranked),
            new StubEmbeddingRepository(similarChunkIds: []),
            embeddingClient,
            Options.Create(new HybridSearchOptions()),
            Options.Create(new EmbeddingConfig { ModelName = "test-model" }),
            NullLogger<HybridChunkSearcher>.Instance
        );
    }
}
