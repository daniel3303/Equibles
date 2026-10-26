using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Microsoft.Extensions.Options;

namespace Equibles.IntegrationTests.Media;

/// <summary>
/// A buffered save followed by one store sync lands the blob in the filesystem store, stamped on
/// the row and readable through the regular file manager that shares the scope's router.
/// </summary>
public class BufferedFileWriterTests
{
    [Fact]
    public async Task SaveInternalFileBuffered_ThenSyncStore_IsReadableThroughTheFileManager()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "equibles-buffered-" + Guid.NewGuid().ToString("N")
        );
        try
        {
            var repository = new FileRepository(
                TestDbContextFactory.Create(new MediaModuleConfiguration())
            );
            var options = Options.Create(
                new FileStorageOptions { Enabled = true, RootPath = root }
            );
            var router = new FileStorageRouter(
                new DatabaseFileStorageProvider(),
                new FileSystemFileStorageProvider(options),
                options
            );
            var writer = new BufferedFileWriter(repository, router);
            byte[] content = [0x89, 0x50, 0x4E, 0x47, 0x01, 0x02, 0x03];

            var file = await writer.SaveInternalFileBuffered(
                content,
                "deck001",
                "png",
                "image/png"
            );
            writer.SyncStore();

            file.StorageProvider.Should().Be(StorageProvider.FileSystem);
            file.RelativePath.Should().NotBeNullOrEmpty();
            var reader = FileManagerTestFactory.Create(repository, options.Value);
            (await reader.GetContent(file)).Should().Equal(content);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
