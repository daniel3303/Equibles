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
/// <see cref="DocumentProcessorWorker.DoWork"/> was entirely uncovered. With an
/// empty database and embeddings disabled, both per-cycle phases find no work:
/// the chunking loop breaks on the first <c>ChunkDocumentBatch</c> returning
/// false and the embedding loop breaks on <c>GenerateEmbeddingBatch</c>'s
/// not-configured early-out — exercising the full two-phase orchestration
/// (both loops, both breaks, both phase-complete logs) end-to-end through the
/// real scope/DB harness.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class DocumentProcessorWorkerDoWorkTests : ParadeDbMcpTestBase
{
    public DocumentProcessorWorkerDoWorkTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task DoWork_NoPendingDocumentsAndEmbeddingsDisabled_CompletesBothPhases()
    {
        var documentManager = new DocumentManager(
            new DocumentRepository(DbContext),
            new ChunkRepository(DbContext),
            Substitute.For<IDocumentProcessor>(),
            Options.Create(new EmbeddingConfig { Enabled = false }),
            Substitute.For<ILogger<DocumentManager>>()
        );

        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(DocumentManager), documentManager)
        );

        var worker = new DocumentProcessorWorker(
            Substitute.For<ILogger<DocumentProcessorWorker>>(),
            scopeFactory,
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        var doWork = typeof(DocumentProcessorWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        // Must complete without throwing — both phases find no work and exit.
        await (Task)doWork.Invoke(worker, [CancellationToken.None]);
    }
}
