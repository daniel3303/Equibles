using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Helpers;

/// <summary>
/// Builds a <see cref="FileStorageRouter"/> for tests. With the store disabled (the default)
/// the router writes to the database, matching tests that assert on inline byte content;
/// pass a root path to exercise the filesystem write path.
/// </summary>
internal static class FileStorageRouterTestFactory
{
    public static FileStorageRouter Disabled()
    {
        return Create(new FileStorageOptions());
    }

    public static FileStorageRouter Create(FileStorageOptions options)
    {
        var wrapped = Options.Create(options);
        return new FileStorageRouter(
            new DatabaseFileStorageProvider(),
            new FileSystemFileStorageProvider(wrapped),
            wrapped
        );
    }
}
