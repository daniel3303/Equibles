using System.Data.Common;
using System.Globalization;
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
    public async Task Search_NarrowsOutdatedMatchesInsideTheIndex_UsingTheFilingDateRangeAndTopK()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        var old = AddDocument(issuer, new DateOnly(2020, 1, 1));
        for (var index = 0; index < 80; index++)
            AddChunk(old, index, "orbital revenue");
        var current = AddDocument(issuer, new DateOnly(2026, 1, 1));
        var expected = AddChunk(
            current,
            0,
            "orbital revenue " + string.Join(' ', Enumerable.Repeat("filler", 200))
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
        capture
            .Scans.Should()
            .ContainSingle("the filing-date range must narrow the index before ranking");
        capture
            .Parameters.Should()
            .Contain(parameter => parameter.Value.ToString().Contains("\"range\""));
        (await ExplainFirstScan(capture)).Should().Contain("TopKScanExecState");
    }

    [Fact]
    public async Task Search_ExcludesForeignFilingsSharingTheSymbol_ByParentOwnership()
    {
        var domestic = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        var foreign = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "SAME");
        foreign.Presentation.Listing.MarketCountryCode = "PT";
        foreign.Presentation.Listing.MarketIdentifierCode = "XLIS";
        var wrongOwner = AddDocument(foreign, new DateOnly(2026, 1, 1));
        for (var index = 0; index < 80; index++)
            AddChunk(wrongOwner, index, "orbital revenue");
        var expected = AddChunk(
            AddDocument(domestic, new DateOnly(2026, 1, 1)),
            0,
            "orbital revenue " + string.Join(' ', Enumerable.Repeat("filler", 200))
        );
        await DbContext.SaveChangesAsync();

        var result = await new ChunkRepository(DbContext).HybridSearch(
            "orbital revenue",
            1,
            ticker: "SAME",
            startDate: new DateOnly(2025, 1, 1)
        );

        result.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task Search_ReturnsEmptyWithoutScanningTheIndex_WhenTheParentWindowIsEmpty()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "EMPTY");
        var old = AddDocument(issuer, new DateOnly(2020, 1, 1));
        for (var index = 0; index < 80; index++)
            AddChunk(old, index, "orbital revenue");
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
    public async Task Search_LargeDateWindowRanksOnce_WithTheRangeInsideTheIndex()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "LARGE");
        var old = AddDocument(issuer, new DateOnly(2020, 1, 1));
        for (var index = 0; index < 80; index++)
            AddChunk(old, index, "orbital revenue");
        for (var index = 0; index < 4097; index++)
            AddDocument(issuer, new DateOnly(2026, 1, 1));
        var expected = AddChunk(
            AddDocument(issuer, new DateOnly(2026, 1, 1)),
            0,
            "orbital revenue " + string.Join(' ', Enumerable.Repeat("filler", 200))
        );
        await DbContext.SaveChangesAsync();
        var capture = new RankCapture();
        await using var context = Fixture.CreateDbContext(builder =>
            builder.AddInterceptors(capture)
        );

        var result = await new ChunkRepository(context).HybridSearch(
            "orbital revenue",
            1,
            startDate: new DateOnly(2025, 1, 1)
        );

        result.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
        capture.Scans.Should().ContainSingle("a wide window is a range, not a document list");
        capture
            .Parameters.Should()
            .Contain(parameter => parameter.Value.ToString().Contains("\"range\""))
            .And.NotContain(parameter => parameter.Value.ToString().Contains("term_set"));
        (await ExplainFirstScan(capture)).Should().Contain("TopKScanExecState");
    }

    [Fact]
    public async Task Search_EndDateIncludesItsWholeDay_AndExcludesTheNextDay()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "EDGE");
        var lastDay = AddChunk(AddDocument(issuer, new DateOnly(2026, 2, 1)), 0, "orbital revenue");
        AddChunk(AddDocument(issuer, new DateOnly(2026, 2, 2)), 0, "orbital revenue");
        var firstDay = AddChunk(
            AddDocument(issuer, new DateOnly(2026, 1, 1)),
            0,
            "orbital revenue"
        );
        AddChunk(AddDocument(issuer, new DateOnly(2025, 12, 31)), 0, "orbital revenue");
        await DbContext.SaveChangesAsync();

        var result = await new ChunkRepository(DbContext).HybridSearch(
            "orbital revenue",
            10,
            startDate: new DateOnly(2026, 1, 1),
            endDate: new DateOnly(2026, 2, 1)
        );

        result.Select(chunk => chunk.Id).Should().BeEquivalentTo([lastDay.Id, firstDay.Id]);
    }

    [Fact]
    public async Task Search_MaxEndDate_IsAnOpenUpperBound()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "OPEN");
        var hit = AddChunk(AddDocument(issuer, new DateOnly(2026, 2, 1)), 0, "orbital revenue");
        AddChunk(AddDocument(issuer, new DateOnly(2025, 12, 31)), 0, "orbital revenue");
        await DbContext.SaveChangesAsync();

        var result = await new ChunkRepository(DbContext).HybridSearch(
            "orbital revenue",
            10,
            startDate: new DateOnly(2026, 1, 1),
            endDate: DateOnly.MaxValue
        );

        result.Select(chunk => chunk.Id).Should().Equal(hit.Id);
    }

    // The window reaches ParadeDB as text; a culture-formatted time ("00.00.00") is rejected and
    // a Buddhist-calendar year matches nothing.
    [Theory]
    [InlineData("fi-FI")]
    [InlineData("th-TH")]
    public async Task Search_DateWindow_IsCultureInvariant(string culture)
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "LOCALE");
        var hit = AddChunk(AddDocument(issuer, new DateOnly(2026, 2, 1)), 0, "orbital revenue");
        AddChunk(AddDocument(issuer, new DateOnly(2025, 12, 31)), 0, "orbital revenue");
        await DbContext.SaveChangesAsync();
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            var result = await new ChunkRepository(DbContext).HybridSearch(
                "orbital revenue",
                10,
                startDate: new DateOnly(2026, 1, 1),
                endDate: new DateOnly(2026, 2, 1)
            );

            result.Select(chunk => chunk.Id).Should().Equal(hit.Id);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public async Task Search_RanksTextAcrossDocumentTypes_WithoutBoostingRareTypes()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "RANK");
        var annual = AddDocument(issuer, new DateOnly(2026, 1, 1));
        var expected = AddChunk(annual, 0, "orbital revenue", DateTime.UtcNow);
        for (var index = 1; index <= 40; index++)
            AddChunk(annual, index, "unrelated filing text", DateTime.UtcNow);
        var quarterly = AddDocument(issuer, new DateOnly(2026, 1, 1));
        quarterly.DocumentType = DocumentType.TenQ;
        AddChunk(
            quarterly,
            0,
            "orbital revenue " + string.Join(' ', Enumerable.Repeat("filler", 4)),
            DateTime.UtcNow
        );
        await DbContext.SaveChangesAsync();

        var result = await new ChunkRepository(DbContext).HybridSearch(
            "orbital revenue",
            1,
            documentTypes: [DocumentType.TenK, DocumentType.TenQ]
        );

        result.Should().ContainSingle().Which.Id.Should().Be(expected.Id);
    }

    [Fact]
    public async Task Search_DateWindowFollowsTheParentDate_WhenAWriterPassesAStaleChunkDate()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "DATES");
        var tooNew = AddDocument(issuer, new DateOnly(2026, 3, 1));
        AddChunk(tooNew, 0, "orbital revenue", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var wrongType = AddDocument(issuer, new DateOnly(2026, 1, 1));
        wrongType.DocumentType = DocumentType.TenQ;
        AddChunk(
            wrongType,
            0,
            "orbital revenue",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        var expected = AddChunk(
            AddDocument(issuer, new DateOnly(2026, 1, 1)),
            0,
            "orbital revenue",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        );
        await DbContext.SaveChangesAsync();

        var result = await new ChunkRepository(DbContext).HybridSearch(
            "orbital revenue",
            3,
            documentTypes: [DocumentType.TenK],
            startDate: new DateOnly(2026, 1, 1),
            endDate: new DateOnly(2026, 2, 1)
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

    // writerDate stands in for a writer that passes a date other than the parent's; the chunk
    // trigger replaces it, so every seeded chunk carries its document's filing date.
    private Chunk AddChunk(
        Document document,
        int index,
        string content,
        DateTime? writerDate = null
    )
    {
        var chunk = new Chunk
        {
            Document = document,
            Index = index,
            Content = content,
            Ticker = "SAME",
            DocumentType = document.DocumentType,
            ReportingDate =
                writerDate
                ?? DateTime.SpecifyKind(
                    document.ReportingDate.ToDateTime(TimeOnly.MinValue),
                    DateTimeKind.Utc
                ),
        };
        DbContext.Add(chunk);
        return chunk;
    }

    private async Task<string> ExplainFirstScan(RankCapture capture)
    {
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
        return string.Join('\n', plan);
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
