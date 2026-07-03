using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.BusinessLogic.Configuration;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Equibles.IntegrationTests.Media;

/// <summary>
/// Proves images take the same chokepoint as every other blob: with the filesystem store
/// enabled, a saved image is written to disk and carries no inline database bytes.
/// </summary>
public class ImageManagerFilesystemStoreTests
{
    private static byte[] Png(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task SaveImage_WhenStoreEnabled_WritesToDiskWithNoDatabaseBytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "eq-img-fs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var context = TestDbContextFactory.Create(new MediaModuleConfiguration());
            var router = FileStorageRouterTestFactory.Create(
                new FileStorageOptions { Enabled = true, RootPath = root }
            );
            var sut = new ImageManager(new ImageRepository(context), router);

            var image = await sut.SaveImage(Png(8, 8), "logo.png", null, null);

            image.StorageProvider.Should().Be(StorageProvider.FileSystem);
            image.FileContent.Should().BeNull();
            image.RelativePath.Should().StartWith("blob/sha256/");
            System
                .IO.File.Exists(
                    Path.Combine(root, image.RelativePath.Replace('/', Path.DirectorySeparatorChar))
                )
                .Should()
                .BeTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
