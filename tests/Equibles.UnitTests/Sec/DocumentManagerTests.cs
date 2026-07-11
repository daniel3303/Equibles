using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.Data.Models.Chunks;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Tests for <see cref="DocumentManager"/>.
/// ChunkDocumentBatch uses .Include(d => d.Chunks) which requires PostgreSQL (pgvector),
/// so we focus on the embedding configuration logic and GenerateEmbeddingBatch guard clauses.
/// </summary>
public class DocumentManagerTests
{
    [Fact]
    public async Task GenerateEmbeddingBatch_EmbeddingsNotConfigured_ReturnsFalse()
    {
        var embeddingConfig = Options.Create(new EmbeddingConfig { Enabled = false });
        var logger = Substitute.For<ILogger<DocumentManager>>();
        var processor = Substitute.For<IDocumentProcessor>();

        var sut = new DocumentManager(null, null, null, processor, embeddingConfig, logger);

        var result = await sut.GenerateEmbeddingBatch(
            new BackfillCursor("test"),
            CancellationToken.None
        );

        result.Should().BeFalse();
        await processor
            .DidNotReceive()
            .GenerateEmbeddings(Arg.Any<List<Chunk>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GenerateEmbeddingBatch_EnabledButNoBaseUrl_ReturnsFalse()
    {
        var embeddingConfig = Options.Create(
            new EmbeddingConfig
            {
                Enabled = true,
                BaseUrl = null,
                ModelName = "all-MiniLM-L6-v2",
            }
        );
        var logger = Substitute.For<ILogger<DocumentManager>>();
        var processor = Substitute.For<IDocumentProcessor>();

        var sut = new DocumentManager(null, null, null, processor, embeddingConfig, logger);

        var result = await sut.GenerateEmbeddingBatch(
            new BackfillCursor("test"),
            CancellationToken.None
        );

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateEmbeddingBatch_EnabledButNoModelName_ReturnsFalse()
    {
        var embeddingConfig = Options.Create(
            new EmbeddingConfig
            {
                Enabled = true,
                BaseUrl = "http://localhost:8080",
                ModelName = null,
            }
        );
        var logger = Substitute.For<ILogger<DocumentManager>>();
        var processor = Substitute.For<IDocumentProcessor>();

        var sut = new DocumentManager(null, null, null, processor, embeddingConfig, logger);

        var result = await sut.GenerateEmbeddingBatch(
            new BackfillCursor("test"),
            CancellationToken.None
        );

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateEmbeddingBatch_FullRescanQueryThrows_RewindsTheStampAndRethrows()
    {
        // The full-rescan stamp is persisted BEFORE the unfloored scan runs (crash-loop
        // protection), so without the rewind a scan that faults — the minutes-long chunk
        // anti-join exceeding the command timeout — silently consumes the whole daily slot,
        // and rows behind the bounded window starve a day per fault, indefinitely under a
        // recurring timeout. A failed scan must be re-admitted after the short failure
        // interval instead, and the fault must still reach the worker's error ladder.
        var embeddingConfig = Options.Create(
            new EmbeddingConfig
            {
                Enabled = true,
                BaseUrl = "http://localhost:8080",
                ModelName = "test-model",
            }
        );
        var chunkRepository = Substitute.For<ChunkRepository>(
            (Equibles.Data.EquiblesFinancialDbContext)null
        );
        chunkRepository
            .When(r => r.GetAll())
            .Do(_ => throw new InvalidOperationException("query timeout"));
        var backfillStateRepository = Substitute.For<BackfillStateRepository>(
            (Equibles.Data.EquiblesFinancialDbContext)null
        );

        var sut = new DocumentManager(
            null,
            chunkRepository,
            backfillStateRepository,
            Substitute.For<IDocumentProcessor>(),
            embeddingConfig,
            Substitute.For<ILogger<DocumentManager>>()
        );
        var cursor = new BackfillCursor("chunk-embedding");

        var act = () => sut.GenerateEmbeddingBatch(cursor, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*query timeout*");
        cursor
            .LastFullRescanAt.Should()
            .BeCloseTo(DateTime.UtcNow.AddDays(-1).AddMinutes(30), TimeSpan.FromMinutes(2));
    }

    // ═══════════════════════════════════════════════════════════════════
    // EmbeddingConfig — IsConfigured logic
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void EmbeddingConfig_AllFieldsSet_IsConfiguredTrue()
    {
        var config = new EmbeddingConfig
        {
            Enabled = true,
            BaseUrl = "http://localhost:8080",
            ModelName = "all-MiniLM-L6-v2",
            ApiKey = "key123",
        };

        config.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void EmbeddingConfig_DisabledWithAllFields_IsConfiguredFalse()
    {
        var config = new EmbeddingConfig
        {
            Enabled = false,
            BaseUrl = "http://localhost:8080",
            ModelName = "all-MiniLM-L6-v2",
        };

        config.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void EmbeddingConfig_DefaultBatchSize_Is10()
    {
        var config = new EmbeddingConfig();

        config.BatchSize.Should().Be(10);
    }

    [Theory]
    [InlineData(true, "http://localhost", "model", true)]
    [InlineData(true, "", "model", false)]
    [InlineData(true, "http://localhost", "", false)]
    [InlineData(true, null, "model", false)]
    [InlineData(false, "http://localhost", "model", false)]
    public void EmbeddingConfig_IsConfigured_CombinationMatrix(
        bool enabled,
        string baseUrl,
        string modelName,
        bool expected
    )
    {
        var config = new EmbeddingConfig
        {
            Enabled = enabled,
            BaseUrl = baseUrl,
            ModelName = modelName,
        };

        config.IsConfigured.Should().Be(expected);
    }
}
