using System.Reflection;
using Equibles.Errors.BusinessLogic;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.BusinessLogic.Embeddings;
using Equibles.Sec.BusinessLogic.Processing;
using Equibles.Sec.HostedService;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// <see cref="DocumentProcessorWorker.DoWork"/> was entirely uncovered. Two paths matter:
/// <para>
/// With embeddings enabled the cycle runs its two phases; against an empty database the
/// chunking loop breaks on the first <c>ChunkDocumentBatch</c> returning false and the
/// embedding loop breaks on <c>GenerateEmbeddingBatch</c>'s not-configured early-out (an
/// enabled config with no base URL is still not configured), so the full two-phase
/// orchestration runs end-to-end through the real scope/DB harness.
/// </para>
/// <para>
/// With embeddings disabled the cycle must short-circuit before chunking: chunks exist only
/// to carry embeddings, so building them can never produce work.
/// </para>
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentProcessorWorkerDoWorkTests : ParadeDbMcpTestBase
{
    public DocumentProcessorWorkerDoWorkTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose() { }
        }
    }

    private (DocumentProcessorWorker Worker, List<string> Messages) CreateWorker(
        EmbeddingConfig embeddingConfig
    )
    {
        var documentManager = new DocumentManager(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            new BackfillStateRepository(DbContext),
            Substitute.For<IDocumentProcessor>(),
            Options.Create(embeddingConfig),
            Substitute.For<ILogger<DocumentManager>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DocumentManager), documentManager),
            (typeof(IOptions<EmbeddingConfig>), Options.Create(embeddingConfig))
        );

        var logger = new CapturingLogger<DocumentProcessorWorker>();
        var worker = new DocumentProcessorWorker(
            logger,
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        return (worker, logger.Messages);
    }

    private static Task RunDoWork(DocumentProcessorWorker worker)
    {
        var doWork = typeof(DocumentProcessorWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        return (Task)doWork.Invoke(worker, [CancellationToken.None]);
    }

    [Fact]
    public async Task DoWork_NoPendingDocumentsAndEmbeddingsConfigured_CompletesBothPhases()
    {
        // Enabled but not configured: no base URL, so the embedding loop early-outs on the
        // first batch instead of reaching a server.
        var (worker, messages) = CreateWorker(new EmbeddingConfig { Enabled = true });

        await RunDoWork(worker);

        Assert.Contains(messages, m => m.Contains("Phase 1: Chunking all pending documents"));
        Assert.Contains(messages, m => m.Contains("Phase 1 complete: All documents chunked"));
        Assert.Contains(messages, m => m.Contains("Phase 2: Generating all pending embeddings"));
        Assert.Contains(messages, m => m.Contains("Phase 2 complete: All embeddings generated"));
    }

    [Fact]
    public async Task DoWork_EmbeddingsDisabled_SkipsBothPhases()
    {
        var (worker, messages) = CreateWorker(new EmbeddingConfig { Enabled = false });

        await RunDoWork(worker);

        Assert.Contains(
            messages,
            m => m.Contains("Document processing skipped: Embedding:Enabled is false")
        );
        Assert.DoesNotContain(messages, m => m.Contains("Phase 1"));
        Assert.DoesNotContain(messages, m => m.Contains("Phase 2"));
    }
}
