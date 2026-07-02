using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Repositories;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Media;

/// <summary>
/// Builds a <see cref="FileManager"/> wired with the real storage providers for tests
/// that exercise the default (database) write path. The filesystem store is left
/// disabled unless the caller supplies options that enable it.
/// </summary>
internal static class FileManagerTestFactory
{
    public static FileManager Create(FileRepository repository, FileStorageOptions options = null)
    {
        var wrapped = Options.Create(options ?? new FileStorageOptions());
        return new FileManager(
            repository,
            Substitute.For<PendingBlobDeletionRepository>((EquiblesFinancialDbContext)null),
            new DatabaseFileStorageProvider(),
            new FileSystemFileStorageProvider(wrapped),
            wrapped
        );
    }
}
