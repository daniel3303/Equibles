using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Equibles.IntegrationTests.Media;

public class ImageManagerSaveImageMaxBoundsTests
{
    private readonly ImageManager _sut;

    public ImageManagerSaveImageMaxBoundsTests()
    {
        var context = TestDbContextFactory.Create(new MediaModuleConfiguration());
        _sut = new ImageManager(new ImageRepository(context));
    }

    private static byte[] CreateMinimalPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    // Contract: maxWidth/maxHeight are documented as the *maximum* width/height.
    // A source already within those bounds must not be enlarged — a 10x10
    // image saved with a 4000x4000 cap should stay 10x10, not be upscaled
    // (which wastes storage and blurs the image).
    [Fact]
    public async Task SaveImage_SourceSmallerThanMax_IsNotUpscaled()
    {
        var content = CreateMinimalPng(10, 10);

        var image = await _sut.SaveImage(content, "small.png", 4000, 4000);

        image.Width.Should().Be(10);
        image.Height.Should().Be(10);
    }
}
