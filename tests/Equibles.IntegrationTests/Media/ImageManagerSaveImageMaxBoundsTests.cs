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
        _sut = new ImageManager(
            new ImageRepository(context),
            FileStorageRouterTestFactory.Disabled()
        );
    }

    private static byte[] CreateMinimalPng(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    private static byte[] CreateMinimalJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
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

    // The bounds are a bounding BOX, not a target size. The plain Resize(w, h)
    // overload stretches to the exact box when both bounds are set, which
    // recorded a 2408x1648 upload as 2400x2400 in production.
    [Fact]
    public async Task SaveImage_SourceExceedingMax_PreservesTheAspectRatio()
    {
        var content = CreateMinimalPng(100, 50);

        var image = await _sut.SaveImage(content, "wide.png", 40, 40);

        image.Width.Should().Be(40);
        image.Height.Should().Be(20);
    }

    // The stored bytes must BE the resized image. Recording resized dimensions
    // while storing the original bytes shipped blobs that disagreed with their
    // metadata — and meant the cap never shrank anything on disk.
    [Fact]
    public async Task SaveImage_SourceExceedingMax_StoresTheResizedBytes()
    {
        var content = CreateMinimalPng(100, 50);

        var image = await _sut.SaveImage(content, "wide.png", 40, 40);

        using var stored = SixLabors.ImageSharp.Image.Load(image.FileContent.Bytes);
        stored.Width.Should().Be(image.Width);
        stored.Height.Should().Be(image.Height);
        image.Size.Should().Be(image.FileContent.Bytes.Length);
    }

    // A single bound leaves the other dimension derived from the aspect ratio.
    [Fact]
    public async Task SaveImage_SingleBound_DerivesTheOtherDimension()
    {
        var content = CreateMinimalPng(100, 50);

        var image = await _sut.SaveImage(content, "wide.png", 40, null);

        image.Width.Should().Be(40);
        image.Height.Should().Be(20);
    }

    // Within bounds nothing is re-encoded: the caller's exact bytes are stored,
    // so an already-fitting upload is never degraded or re-compressed.
    [Fact]
    public async Task SaveImage_SourceWithinBounds_StoresTheOriginalBytesUntouched()
    {
        var content = CreateMinimalPng(10, 10);

        var image = await _sut.SaveImage(content, "small.png", 4000, 4000);

        image.FileContent.Bytes.Should().Equal(content);
    }

    // A resized image is re-encoded in the format it arrived in — a resized JPEG
    // must not come back as a PNG wearing a .jpg extension and image/jpeg type.
    [Fact]
    public async Task SaveImage_ResizedJpeg_IsStoredAsJpeg()
    {
        var content = CreateMinimalJpeg(100, 50);

        var image = await _sut.SaveImage(content, "photo.jpg", 40, 40);

        var format = SixLabors.ImageSharp.Image.DetectFormat(image.FileContent.Bytes);
        format.Name.Should().Be("JPEG");
        image.ContentType.Should().Be("image/jpeg");
    }
}
