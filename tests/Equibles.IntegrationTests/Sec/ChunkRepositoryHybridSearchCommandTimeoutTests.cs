using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins #1026: <see cref="ChunkRepository.HybridSearch"/> must run under a
/// hard <c>CommandTimeout</c> matching <c>SearchAggregator.ProviderTimeout</c>.
/// Without it, <c>pdb.parse</c> / <c>pdb.score</c> ignore the cancellation
/// token mid-execution and the Npgsql connection stays pinned for minutes
/// after the aggregator has already returned Empty. An EF Core command
/// interceptor captures the <c>CommandTimeout</c> Npgsql receives for the
/// BM25 SELECT, and the test asserts it equals the budgeted ceiling — and
/// that the value is restored on the DbContext afterwards.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ChunkRepositoryHybridSearchCommandTimeoutTests : ParadeDbMcpTestBase
{
    public ChunkRepositoryHybridSearchCommandTimeoutTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task HybridSearch_AppliesFiveSecondCommandTimeoutToTheBm25Query()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple Inc.",
            Cik = "0000320193",
        };
        DbContext.Add(stock);
        var doc = SeedDocument(stock, new DateOnly(2026, 1, 15));
        SeedChunk(doc, "Services revenue grew substantially this quarter.", stock.Ticker);
        await DbContext.SaveChangesAsync();

        // Fresh DbContext with a capturing interceptor wired in. The
        // production code does not expose any hook to read the timeout on
        // the actual SELECT — only the underlying DbCommand sees it — so
        // the interceptor is the only way to pin this property.
        var interceptor = new CapturingCommandTimeoutInterceptor();
        await using var instrumentedContext = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(interceptor)
        );
        var initialTimeout = instrumentedContext.Database.GetCommandTimeout();
        var sut = new ChunkRepository(instrumentedContext);

        await sut.HybridSearch(searchText: "services revenue", maxResults: 10);

        var bm25Timeout = interceptor.GetSelectTimeoutForChunkTable();
        bm25Timeout
            .Should()
            .Be(
                5,
                "the chunk BM25 query must run under a 5-second statement timeout so Postgres aborts the plan independently of pdb.parse / pdb.score cooperative cancellation (#1026)"
            );
        instrumentedContext
            .Database.GetCommandTimeout()
            .Should()
            .Be(
                initialTimeout,
                "HybridSearch must restore the prior CommandTimeout so other queries sharing this DbContext aren't capped"
            );
    }

    private Document SeedDocument(CommonStock stock, DateOnly reportingDate)
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
            ReportingDate = reportingDate,
            ReportingForDate = reportingDate.AddDays(-30),
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

    // Records every (CommandText, CommandTimeout) tuple the DbContext sends
    // to Npgsql. The BM25 query is identified by its SELECT against the
    // Chunk table — the only one in this test's command stream that joins
    // pdb.parse / pdb.score.
    private sealed class CapturingCommandTimeoutInterceptor : DbCommandInterceptor
    {
        private readonly List<(string CommandText, int Timeout)> _observed = new();

        public int? GetSelectTimeoutForChunkTable() =>
            _observed
                .Where(o =>
                    o.CommandText.Contains("\"Chunk\"", StringComparison.Ordinal)
                    && o.CommandText.StartsWith("SELECT", StringComparison.Ordinal)
                )
                .Select(o => (int?)o.Timeout)
                .LastOrDefault();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            _observed.Add((command.CommandText, command.CommandTimeout));
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            _observed.Add((command.CommandText, command.CommandTimeout));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
