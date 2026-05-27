using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the fix for #2157: <see cref="ChunkRepository.HybridSearch"/> must push the
/// ticker filter INTO the BM25 index (a Tantivy <c>term</c> inside the <c>@@@</c>
/// query) rather than post-filtering scored chunks with a SQL
/// <c>WHERE "Ticker" = ...</c> predicate. The SQL form compiles to a ParadeDB
/// <c>heap_filter</c> that scores every text match before filtering — for a
/// high-coverage ticker that scored set is enormous and blows the 5s budget.
///
/// Two guarantees are pinned here:
/// 1. <see cref="HybridSearch_WithSeparatorTicker_StillMatchesViaRawTokenizer"/> —
///    correctness for class-share tickers (e.g. <c>BRK-B</c>). The default tokenizer
///    splits on dash/slash, so an exact term filter only works because the column is
///    indexed with the <c>raw</c> tokenizer.
/// 2. <see cref="HybridSearch_TickerFilter_IsPushedIntoTheBm25Query_NotASqlHeapFilter"/> —
///    the filter rides inside the single <c>@@@ ...::jsonb</c> predicate and never
///    reappears as a standalone SQL column comparison.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ChunkRepositoryHybridSearchTickerFilterTests : ParadeDbMcpTestBase
{
    private readonly ITestOutputHelper _output;

    public ChunkRepositoryHybridSearchTickerFilterTests(
        ParadeDbFixture fixture,
        ITestOutputHelper output
    )
        : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task HybridSearch_WithTickerFilter_ReturnsOnlyChunksForThatTicker()
    {
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        var microsoft = SeedStock("MSFT", "Microsoft Corp.", "0000789019");
        SeedChunk(SeedDocument(apple), "Services revenue grew substantially this quarter.", "AAPL");
        SeedChunk(SeedDocument(microsoft), "Services revenue rose across the cloud unit.", "MSFT");
        await DbContext.SaveChangesAsync();

        var sut = new ChunkRepository(DbContext);

        var results = await sut.HybridSearch("services revenue", maxResults: 10, ticker: "AAPL");

        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(c => c.Ticker.Should().Be("AAPL"));
    }

    [Fact]
    public async Task HybridSearch_WithSeparatorTicker_StillMatchesViaRawTokenizer()
    {
        // The default BM25 tokenizer splits "BRK-B" into "brk"/"b", which would make a
        // single exact term filter impossible. The raw tokenizer keeps it as one token.
        var berkshire = SeedStock("BRK-B", "Berkshire Hathaway Inc.", "0001067983");
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        SeedChunk(SeedDocument(berkshire), "Insurance services revenue increased.", "BRK-B");
        SeedChunk(SeedDocument(apple), "Services revenue grew this quarter.", "AAPL");
        await DbContext.SaveChangesAsync();

        var sut = new ChunkRepository(DbContext);

        var results = await sut.HybridSearch("services revenue", maxResults: 10, ticker: "BRK-B");

        results.Should().NotBeEmpty("the raw tokenizer must let a class-share ticker match");
        results.Should().AllSatisfy(c => c.Ticker.Should().Be("BRK-B"));
    }

    [Fact]
    public async Task HybridSearch_TickerFilter_IsPushedIntoTheBm25Query_NotASqlHeapFilter()
    {
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        SeedChunk(SeedDocument(apple), "Services revenue grew substantially this quarter.", "AAPL");
        await DbContext.SaveChangesAsync();

        var interceptor = new CapturingCommandInterceptor();
        await using var instrumentedContext = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(interceptor)
        );
        var sut = new ChunkRepository(instrumentedContext);

        await sut.HybridSearch("services revenue", maxResults: 10, ticker: "AAPL");

        var sql = interceptor.GetChunkSelectCommandText();
        _output.WriteLine(sql);

        sql.Should().NotBeNull();
        sql.Should().Contain("@@@", "the filter must ride inside the BM25 search operator");
        sql.Should().Contain("jsonb", "the boolean query is passed as a ::jsonb predicate");
        sql.Should()
            .NotContain(
                "\"Ticker\" =",
                "the ticker filter must live inside the BM25 query, not a SQL heap-filter predicate that re-introduces #2157"
            );
    }

    private CommonStock SeedStock(string ticker, string name, string cik)
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = name,
            Cik = cik,
        };
        DbContext.Add(stock);
        return stock;
    }

    private Document SeedDocument(CommonStock stock)
    {
        var fileContent = new FileContent { Bytes = "placeholder"u8.ToArray() };
        var file = new File
        {
            Name = "filing",
            Extension = "txt",
            ContentType = "text/plain",
            Size = fileContent.Bytes.Length,
            FileContent = fileContent,
        };
        fileContent.FileId = file.Id;
        DbContext.Add(file);

        var document = new Document
        {
            CommonStock = stock,
            CommonStockId = stock.Id,
            Content = file,
            ContentId = file.Id,
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 1, 15),
            ReportingForDate = new DateOnly(2025, 12, 31),
            LineCount = 1,
        };
        DbContext.Add(document);
        return document;
    }

    private void SeedChunk(Document document, string content, string ticker)
    {
        DbContext.Add(
            new Chunk
            {
                Document = document,
                DocumentId = document.Id,
                Index = 0,
                StartPosition = 0,
                EndPosition = content.Length,
                StartLineNumber = 1,
                Content = content,
                DocumentType = document.DocumentType,
                Ticker = ticker,
                ReportingDate = DateTime.SpecifyKind(
                    document.ReportingDate.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Utc
                ),
            }
        );
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = new();

        public string GetChunkSelectCommandText() =>
            _commands.LastOrDefault(c =>
                c.StartsWith("SELECT", StringComparison.Ordinal)
                && c.Contains("\"Chunk\"", StringComparison.Ordinal)
            );

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            _commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
