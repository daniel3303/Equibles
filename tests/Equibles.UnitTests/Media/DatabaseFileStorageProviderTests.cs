using System.Text;
using Equibles.Media.BusinessLogic.Storage;
using Equibles.Media.Data.Models;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.UnitTests.Media;

public class DatabaseFileStorageProviderTests
{
    // The database provider preserves the original behavior: bytes go into a FileContent
    // row and the File is stamped Database with its size — no path/hash.
    [Fact]
    public async Task Save_StoresBytesInFileContent_AndStampsDatabaseMetadata()
    {
        var provider = new DatabaseFileStorageProvider();
        var file = new File();
        var content = Encoding.UTF8.GetBytes("hello database");

        await provider.Save(file, content, FileStorageTiers.Blob);

        file.StorageProvider.Should().Be(StorageProvider.Database);
        file.Size.Should().Be(content.Length);
        file.FileContent.Should().NotBeNull();
        file.FileContent.Bytes.Should().Equal(content);
        file.RelativePath.Should().BeNull();
    }

    [Fact]
    public async Task GetContent_ReturnsStoredBytes()
    {
        var provider = new DatabaseFileStorageProvider();
        var file = new File();
        var content = Encoding.UTF8.GetBytes("round trip");
        await provider.Save(file, content, FileStorageTiers.Blob);

        var read = await provider.GetContent(file);

        read.Should().Equal(content);
    }
}
