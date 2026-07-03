using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using Microsoft.Extensions.Options;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Media;

public class FileStorageRouterTests
{
    private static FileStorageRouter Build(bool enabled, string root = null)
    {
        var options = Options.Create(new FileStorageOptions { Enabled = enabled, RootPath = root });
        return new FileStorageRouter(
            new DatabaseFileStorageProvider(),
            new FileSystemFileStorageProvider(options),
            options
        );
    }

    // The whole point of the chokepoint: when the store is enabled there is no way to land
    // new bytes in the database — the write provider is the filesystem, period.
    [Fact]
    public void WriteProvider_WhenStoreEnabled_IsFilesystem()
    {
        Build(enabled: true, root: "/tmp/x")
            .WriteProvider.Provider.Should()
            .Be(StorageProvider.FileSystem);
    }

    // Disabled (a self-hosted deployment without a configured root) still writes to the
    // database, so the app keeps working with no filesystem store.
    [Fact]
    public void WriteProvider_WhenStoreDisabled_IsDatabase()
    {
        Build(enabled: false).WriteProvider.Provider.Should().Be(StorageProvider.Database);
    }

    // Enabling the store writes new content to the filesystem and stamps the row — no
    // FileContent bytes are attached.
    [Fact]
    public async Task Save_WhenEnabled_StampsFilesystemAndLeavesNoDatabaseBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "eq-router-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = new File
            {
                Name = "x",
                Extension = "gz",
                ContentType = "application/gzip",
            };
            await Build(enabled: true, root: root)
                .Save(file, "hello"u8.ToArray(), FileStorageTiers.Blob);

            file.StorageProvider.Should().Be(StorageProvider.FileSystem);
            file.FileContent.Should().BeNull();
            file.RelativePath.Should().StartWith("blob/sha256/");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Reads always follow the provider recorded on the row, independent of the current
    // setting, so content stored under either mode stays reachable.
    [Fact]
    public void ReadProvider_DispatchesOnStoredValue()
    {
        var router = Build(enabled: true, root: "/tmp/x");
        router
            .ReadProvider(StorageProvider.Database)
            .Provider.Should()
            .Be(StorageProvider.Database);
        router
            .ReadProvider(StorageProvider.FileSystem)
            .Provider.Should()
            .Be(StorageProvider.FileSystem);
    }
}
