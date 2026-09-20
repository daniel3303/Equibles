using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the leader-only BM25 scan: <see cref="ChunkRepository.HybridSearch"/> clamps
/// <c>max_parallel_workers_per_gather</c> to zero for exactly the statement carrying the
/// <c>@@@</c> predicate, and the clamp is gone again once the call returns. A pg_search
/// parallel worker that aborts takes the whole Postgres down with it (2026-09-20), and the
/// semi-join a ticker-scoped search compiles to is outside ParadeDB's own row-count guard.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class ChunkRepositoryLeaderOnlyScanTests : ParadeDbMcpTestBase
{
    public ChunkRepositoryLeaderOnlyScanTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task HybridSearch_ClampsParallelWorkersForTheScanAndRevertsAfterwards()
    {
        EquityIssuer apple = SeedStock("AAPL", "Apple Inc.", "0000320193");
        SeedChunk(SeedDocument(apple), "Services revenue grew substantially this quarter.", "AAPL");
        await DbContext.SaveChangesAsync();

        var interceptor = new CapturingCommandInterceptor();
        await using var instrumentedContext = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(interceptor)
        );
        var sut = new ChunkRepository(instrumentedContext);

        var results = await sut.HybridSearch("services revenue", maxResults: 10, ticker: "AAPL");

        results.Should().NotBeEmpty();
        var commands = interceptor.Commands;
        var clamp = commands.FindIndex(c =>
            c.Contains(ChunkRepository.LeaderOnlyScanSql, StringComparison.Ordinal)
        );
        var scan = commands.FindIndex(c => c.Contains("@@@", StringComparison.Ordinal));
        clamp.Should().BeGreaterThanOrEqualTo(0, "the clamp must be issued");
        scan.Should().BeGreaterThan(clamp, "the clamp must precede the BM25 statement");
        instrumentedContext
            .Database.CurrentTransaction.Should()
            .BeNull("the scope owns its transaction");
        var afterwards = await instrumentedContext
            .Database.SqlQueryRaw<string>(
                "SELECT current_setting('max_parallel_workers_per_gather') AS \"Value\""
            )
            .SingleAsync();
        afterwards.Should().NotBe("0", "SET LOCAL must not outlive the scan");
    }

    [Fact]
    public async Task LeaderOnlyScan_InsideACallerTransaction_BorrowsItAndLeavesItOpen()
    {
        await using var context = Fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var sut = new ChunkRepository(context);

        await using (await sut.LeaderOnlyScan())
        {
            var inside = await context
                .Database.SqlQueryRaw<string>(
                    "SELECT current_setting('max_parallel_workers_per_gather') AS \"Value\""
                )
                .SingleAsync();
            inside.Should().Be("0");
        }

        context.Database.CurrentTransaction.Should().BeSameAs(transaction);
    }

    private EquityIssuer SeedStock(string ticker, string name, string cik)
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: ticker,
            Name: name,
            Cik: cik
        );
        DbContext.Add(stock);
        return stock;
    }

    private Document SeedDocument(EquityIssuer stock)
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
            EquityIssuerId = stock.Id,
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

    private Chunk SeedChunk(Document document, string content, string ticker)
    {
        var chunk = new Chunk
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
        };
        DbContext.Add(chunk);
        return chunk;
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        )
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
