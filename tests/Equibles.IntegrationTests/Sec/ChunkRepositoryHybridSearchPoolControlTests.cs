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
/// Pins the pool controls on <see cref="ChunkRepository.HybridSearch"/>: excluded
/// tickers and the multi-document-type filter must resolve INSIDE the BM25 boolean
/// query (a Tantivy <c>must_not</c> / nested <c>should</c> within the <c>@@@</c>
/// predicate), never as SQL heap-filter predicates. Post-filtering scored hits would
/// both re-introduce the #2157 heap-filter blowup and silently shrink the result set
/// instead of refilling it with the next-best matches — the exact failure that let a
/// thesis subject's own filings occupy 36 of the 40 discovery hits for its flagship
/// keyword.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ChunkRepositoryHybridSearchPoolControlTests : ParadeDbMcpTestBase
{
    private readonly ITestOutputHelper _output;

    public ChunkRepositoryHybridSearchPoolControlTests(
        ParadeDbFixture fixture,
        ITestOutputHelper output
    )
        : base(fixture)
    {
        _output = output;
    }

    [Fact]
    public async Task HybridSearch_ExcludeTickers_RefillsWithOtherCompanies()
    {
        // Apple dominates the match set; excluding it must surface the other filers
        // rather than returning a shrunken, Apple-shaped hole.
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        var microsoft = SeedStock("MSFT", "Microsoft Corp.", "0000789019");
        var alphabet = SeedStock("GOOG", "Alphabet Inc.", "0001652044");
        var appleFiling = SeedDocument(apple, DocumentType.TenK);
        for (var index = 0; index < 5; index++)
            SeedChunk(
                appleFiling,
                $"Services revenue grew substantially in segment {index}.",
                "AAPL",
                index
            );
        SeedChunk(
            SeedDocument(microsoft, DocumentType.TenK),
            "Services revenue rose across the cloud unit.",
            "MSFT"
        );
        SeedChunk(
            SeedDocument(alphabet, DocumentType.TenK),
            "Services revenue expanded in the ads unit.",
            "GOOG"
        );
        await DbContext.SaveChangesAsync();

        var sut = new ChunkRepository(DbContext);

        var results = await sut.HybridSearch(
            "services revenue",
            maxResults: 10,
            excludeTickers: ["AAPL"]
        );

        results.Should().NotBeEmpty();
        results.Should().AllSatisfy(c => c.Ticker.Should().NotBe("AAPL"));
        results.Select(c => c.Ticker).Should().Contain(["MSFT", "GOOG"]);
    }

    [Fact]
    public async Task HybridSearch_MultipleDocumentTypes_ReturnsOnlyThoseTypes()
    {
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        SeedChunk(
            SeedDocument(apple, DocumentType.TenK),
            "Services revenue grew in the annual report.",
            "AAPL"
        );
        SeedChunk(
            SeedDocument(apple, DocumentType.TenQ),
            "Services revenue grew in the quarter.",
            "AAPL",
            index: 1
        );
        SeedChunk(
            SeedDocument(apple, DocumentType.EightK),
            "Services revenue grew per the current report.",
            "AAPL",
            index: 2
        );
        await DbContext.SaveChangesAsync();

        var sut = new ChunkRepository(DbContext);

        var results = await sut.HybridSearch(
            "services revenue",
            maxResults: 10,
            documentTypes: [DocumentType.TenK, DocumentType.TenQ]
        );

        results.Should().NotBeEmpty();
        results
            .Select(c => c.DocumentType)
            .Distinct()
            .Should()
            .BeEquivalentTo([DocumentType.TenK, DocumentType.TenQ]);
    }

    [Fact]
    public async Task HybridSearch_PoolControls_RideInsideTheBm25Query_NotSqlHeapFilters()
    {
        var apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        SeedChunk(
            SeedDocument(apple, DocumentType.TenK),
            "Services revenue grew substantially this quarter.",
            "AAPL"
        );
        await DbContext.SaveChangesAsync();

        var interceptor = new CapturingCommandInterceptor();
        await using var instrumentedContext = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(interceptor)
        );
        var sut = new ChunkRepository(instrumentedContext);

        await sut.HybridSearch(
            "services revenue",
            maxResults: 10,
            excludeTickers: ["MSFT", "GOOG"],
            documentTypes: [DocumentType.TenK, DocumentType.TenQ]
        );

        var sql = interceptor.GetChunkSelectCommandText();
        _output.WriteLine(sql);

        sql.Should().NotBeNull();
        sql.Should().Contain("@@@", "the filters must ride inside the BM25 search operator");
        sql.Should().Contain("jsonb", "the boolean query is passed as a ::jsonb predicate");
        // The projection legitimately selects the columns; only PREDICATE forms would
        // re-introduce the #2157 heap filter.
        sql.Should()
            .NotContainAny(
                ["\"Ticker\" <>", "\"Ticker\" NOT IN", "\"Ticker\" ="],
                "the exclusion must live inside the BM25 query, not a SQL heap-filter predicate"
            );
        sql.Should()
            .NotContainAny(
                ["\"DocumentType\" =", "\"DocumentType\" IN"],
                "the type filter must live inside the BM25 query, not a SQL heap-filter predicate"
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

    private Document SeedDocument(CommonStock stock, DocumentType documentType)
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
            DocumentType = documentType,
            ReportingDate = new DateOnly(2026, 1, 15),
            ReportingForDate = new DateOnly(2025, 12, 31),
            LineCount = 1,
        };
        DbContext.Add(document);
        return document;
    }

    private void SeedChunk(Document document, string content, string ticker, int index = 0)
    {
        DbContext.Add(
            new Chunk
            {
                Document = document,
                DocumentId = document.Id,
                Index = index,
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
