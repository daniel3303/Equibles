using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Repositories;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests;

/// <summary>
/// Builds a <see cref="FileManager"/> wired with the real storage providers for tests
/// that exercise the default (database) write path. The filesystem store is left
/// disabled unless the caller supplies options that enable it. Lives in the root
/// namespace so tests in any subfolder can use it without an extra using. Pass a real
/// <see cref="PendingBlobDeletionRepository"/> when a test exercises the delete queue.
/// </summary>
internal static class FileManagerTestFactory
{
    public static FileManager Create(
        FileRepository repository,
        FileStorageOptions options = null,
        PendingBlobDeletionRepository pendingBlobDeletionRepository = null
    )
    {
        var wrapped = Options.Create(options ?? new FileStorageOptions());
        var router = new FileStorageRouter(
            new DatabaseFileStorageProvider(),
            new FileSystemFileStorageProvider(wrapped),
            wrapped
        );
        return new FileManager(
            repository,
            pendingBlobDeletionRepository
                ?? Substitute.For<PendingBlobDeletionRepository>((EquiblesFinancialDbContext)null),
            router
        );
    }
}
