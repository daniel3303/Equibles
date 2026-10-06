using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Media.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class ChunkRepositoryRankedScopeTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    [Fact]
    public async Task Search_NarrowsForeignAndOutdatedMatchesBeforeRanking_UsingParentDatesAndTopK()
    {
        var domestic = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        var foreign = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        foreign.Presentation.Listing.MarketCountryCode = "PT";
        foreign.Presentation.Listing.MarketIdentifierCode = "XLIS";
        var old = AddDocument(domestic, new DateOnly(2020, 1, 1));
        var wrongOwner = AddDocument(foreign, new DateOnly(2026, 1, 1));
        for (var index = 0; index < 80; index++)
        {
            AddChunk(
                old,
                index,
                "orbital revenue",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            );
            AddChunk(
                wrongOwner,
                index,
                "orbital revenue",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            );
        }
        var current = AddDocument(domestic, new DateOnly(2026, 1, 1));
        var expected = AddChunk(
            current,
            0,
            "orbital revenue " + string.Join(' ', Enumerable.Repeat("filler", 200)),
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await DbContext.SaveChangesAsync();
        var capture = new RankCapture();
        await using var context = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(capture)
        );

        var result = await new ChunkRepository(context).HybridSearch(
            "orbital revenue",
            1,
            ticker: "SAME",
            startDate: new DateOnly(2025, 1, 1)
        );

        result.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
        capture.Scans.Should().ContainSingle("the parent scope must narrow the index before ranking");
        capture.Parameters.Should().Contain(parameter => parameter.Value.ToString().Contains("term_set"));
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SET max_parallel_workers_per_gather=0";
        await command.ExecuteNonQueryAsync();
        command.CommandText = "EXPLAIN " + capture.Scans[0];
        foreach (var parameter in capture.Parameters)
            command.Parameters.Add(parameter);
        await using var reader = await command.ExecuteReaderAsync();
        var plan = new List<string>();
        while (await reader.ReadAsync())
            plan.Add(reader.GetString(0));
        string.Join('\n', plan).Should().Contain("TopKScanExecState");
    }

    [Fact]
    public async Task Search_ReturnsEmptyWithoutScanningTheIndex_WhenTheParentWindowIsEmpty()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "EMPTY");
        var old = AddDocument(issuer, new DateOnly(2020, 1, 1));
        for (var index = 0; index < 80; index++)
            AddChunk(
                old,
                index,
                "orbital revenue",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            );
        await DbContext.SaveChangesAsync();
        var capture = new RankCapture();
        await using var context = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(capture)
        );

        var result = await new ChunkRepository(context).HybridSearch(
            "orbital revenue",
            3,
            startDate: new DateOnly(2025, 1, 1)
        );

        result.Should().BeEmpty();
        capture.Scans.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_LargeDateWindowRefillsCandidates_WithoutTruncatingTheDocumentScope()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "LARGE");
        var old = AddDocument(issuer, new DateOnly(2020, 1, 1));
        for (var index = 0; index < 80; index++)
            AddChunk(old, index, "orbital revenue", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        for (var index = 0; index < 4096; index++)
            AddDocument(issuer, new DateOnly(2026, 1, 1));
        var expected = AddChunk(
            AddDocument(issuer, new DateOnly(2026, 1, 1)), 0,
            "orbital revenue " + string.Join(' ', Enumerable.Repeat("filler", 200)),
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await DbContext.SaveChangesAsync();
        var capture = new RankCapture();
        await using var context = Fixture.CreateDbContext(builder => builder.AddInterceptors(capture));

        var result = await new ChunkRepository(context).HybridSearch(
            "orbital revenue", 1, startDate: new DateOnly(2025, 1, 1)
        );

        result.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
        capture.Scans.Count.Should().BeGreaterThan(1);
        capture.Parameters.Should().NotContain(parameter => parameter.Value.ToString().Contains("term_set"));
    }

    [Fact]
    public async Task Search_DateWindowRespectsEndDateAndDocumentType_WithStaleChunkDates()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "DATES");
        var tooNew = AddDocument(issuer, new DateOnly(2026, 3, 1));
        AddChunk(tooNew, 0, "orbital revenue", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var wrongType = AddDocument(issuer, new DateOnly(2026, 1, 1));
        wrongType.DocumentType = DocumentType.TenQ;
        AddChunk(wrongType, 0, "orbital revenue", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var expected = AddChunk(
            AddDocument(issuer, new DateOnly(2026, 1, 1)), 0, "orbital revenue",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await DbContext.SaveChangesAsync();

        var result = await new ChunkRepository(DbContext).HybridSearch(
            "orbital revenue", 3, documentTypes: [DocumentType.TenK],
            startDate: new DateOnly(2026, 1, 1), endDate: new DateOnly(2026, 2, 1)
        );

        result.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_PreservesCallerCancellationAndRestoresTimeout(bool callerCancels)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SLOW");
        AddChunk(
            AddDocument(issuer, new DateOnly(2026, 1, 1)),
            0,
            "orbital revenue",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await DbContext.SaveChangesAsync();
        await using var context = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(new RankCapture(delay: true))
        );
        var originalTimeout = context.Database.GetCommandTimeout();
        using var cancellation = new CancellationTokenSource();
        if (callerCancels)
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
        Func<Task> search = () =>
            new ChunkRepository(context).HybridSearch(
                "orbital revenue",
                1,
                ticker: "SLOW",
                commandTimeoutSeconds: 1,
                cancellationToken: cancellation.Token
            );

        if (callerCancels)
            await search.Should().ThrowAsync<OperationCanceledException>();
        else
            await search.Should().ThrowAsync<ChunkSearchTimeoutException>();
        context.Database.GetCommandTimeout().Should().Be(originalTimeout);
        context.Database.CurrentTransaction.Should().BeNull();
    }

    private Document AddDocument(EquityIssuer issuer, DateOnly date)
    {
        var document = new Document
        {
            Issuer = issuer,
            DocumentType = DocumentType.TenK,
            ReportingDate = date,
            Content = new File
            {
                Name = "filing",
                Extension = "txt",
                ContentType = "text/plain",
                Size = 1,
                FileContent = new FileContent { Bytes = [1] },
            },
        };
        DbContext.Add(document);
        return document;
    }

    private Chunk AddChunk(Document document, int index, string content, DateTime cacheDate)
    {
        var chunk = new Chunk
        {
            Document = document,
            Index = index,
            Content = content,
            Ticker = "SAME",
            DocumentType = document.DocumentType,
            ReportingDate = cacheDate,
        };
        DbContext.Add(chunk);
        return chunk;
    }

    private sealed class RankCapture(bool delay = false) : DbCommandInterceptor
    {
        public List<string> Scans { get; } = [];
        public List<NpgsqlParameter> Parameters { get; } = [];

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (command.CommandText.Contains("@@@", StringComparison.Ordinal))
            {
                if (Scans.Count == 0)
                    foreach (NpgsqlParameter parameter in command.Parameters)
                        Parameters.Add(
                            new NpgsqlParameter(parameter.ParameterName, parameter.NpgsqlDbType)
                            {
                                Value = parameter.Value,
                            }
                        );
                Scans.Add(command.CommandText);
                if (delay)
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }
}
