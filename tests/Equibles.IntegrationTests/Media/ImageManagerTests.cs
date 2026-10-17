using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Tiff;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Equibles.IntegrationTests.Media;

public class ImageManagerTests
{
    private readonly ImageManager _sut;
    private readonly ImageRepository _repository;

    public ImageManagerTests()
    {
        var context = TestDbContextFactory.Create(new MediaModuleConfiguration());
        _repository = new ImageRepository(context);
        // Store disabled → the router persists images in the database, matching these assertions.
        _sut = new ImageManager(_repository, FileStorageRouterTestFactory.Disabled());
    }

    private static byte[] CreateMinimalPng(int width = 10, int height = 10)
    {
        using var image = new Image<Rgba32>(width, height);
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task SaveImage_Png_SetsCorrectExtensionAndMime()
    {
        var content = CreateMinimalPng();

        var image = await _sut.SaveImage(content, "test.png", null, null);

        image.Extension.Should().Be("png");
        image.ContentType.Should().Be("image/png");
        image.Name.Should().Be("test");
    }

    [Fact]
    public async Task SaveImage_Jpg_SetsJpegMime()
    {
        using var img = new Image<Rgba32>(5, 5);
        using var stream = new MemoryStream();
        img.SaveAsJpeg(stream);
        var content = stream.ToArray();

        var image = await _sut.SaveImage(content, "photo.jpg", null, null);

        image.Extension.Should().Be("jpg");
        image.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task SaveImage_NoExtension_ThrowsArgumentException()
    {
        var act = () => _sut.SaveImage([0x01], "filename", null, null);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*extension*");
    }

    [Fact]
    public async Task SaveImage_SizeMatchesContentLength()
    {
        var content = CreateMinimalPng();

        var image = await _sut.SaveImage(content, "test.png", null, null);

        image.Size.Should().Be(content.Length);
    }

    [Fact]
    public async Task SaveImage_CapturesDimensions()
    {
        var content = CreateMinimalPng(20, 15);

        var image = await _sut.SaveImage(content, "test.png", null, null);

        image.Width.Should().Be(20);
        image.Height.Should().Be(15);
    }

    [Fact]
    public async Task SaveImage_WithMaxWidth_ResizesImage()
    {
        var content = CreateMinimalPng(100, 50);

        var image = await _sut.SaveImage(content, "test.png", maxWidth: 10, maxHeight: null);

        image.Width.Should().Be(10);
        image.Height.Should().Be(5); // aspect ratio preserved
    }

    [Fact]
    public async Task SaveImage_WithMaxHeight_ResizesImage()
    {
        var content = CreateMinimalPng(100, 50);

        var image = await _sut.SaveImage(content, "test.png", maxWidth: null, maxHeight: 10);

        image.Height.Should().Be(10);
        image.Width.Should().Be(20); // aspect ratio preserved
    }

    [Fact]
    public async Task SaveImage_WithBothDimensions_ResizesToFit()
    {
        var content = CreateMinimalPng(100, 100);

        var image = await _sut.SaveImage(content, "test.png", maxWidth: 25, maxHeight: 25);

        image.Width.Should().BeInRange(1, 25);
        image.Height.Should().BeInRange(1, 25);
    }

    [Fact]
    public async Task SaveImage_FileContentLinked()
    {
        var content = CreateMinimalPng();

        var image = await _sut.SaveImage(content, "test.png", null, null);

        image.FileContent.Should().NotBeNull();
        image.FileContent.Bytes.Should().BeEquivalentTo(content);
        image.FileContent.File.Should().BeSameAs(image);
    }

    [Fact]
    public async Task SaveImage_EntityAddedToRepository()
    {
        var content = CreateMinimalPng();

        await _sut.SaveImage(content, "test.png", null, null);
        await _repository.SaveChanges();

        _repository.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void DeleteImage_Null_DoesNotThrow()
    {
        var act = () => _sut.DeleteImage(null);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task DeleteImage_ExistingImage_RemovesFromRepository()
    {
        var content = CreateMinimalPng();
        var image = await _sut.SaveImage(content, "test.png", null, null);
        await _repository.SaveChanges();

        _sut.DeleteImage(image);
        await _repository.SaveChanges();

        _repository.GetAll().Should().BeEmpty();
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("webp")]
    [InlineData("bmp")]
    public async Task SaveImage_WebRasterFormat_IsDecoded(string extension)
    {
        var content = Encode(extension);

        var image = await _sut.SaveImage(content, $"logo.{extension}", null, null);

        image.Width.Should().Be(6);
        image.Height.Should().Be(4);
    }

    [Fact]
    public async Task SaveImage_Tiff_IsNotDecoded()
    {
        var content = Encode("tiff");

        var act = () => _sut.SaveImage(content, "scan.tiff", null, null);

        await act.Should().ThrowAsync<UnknownImageFormatException>();
    }

    private static byte[] Encode(string extension)
    {
        IImageEncoder encoder = extension switch
        {
            "gif" => new GifEncoder(),
            "webp" => new WebpEncoder(),
            "bmp" => new BmpEncoder(),
            "tiff" => new TiffEncoder(),
            _ => throw new ArgumentOutOfRangeException(nameof(extension)),
        };
        using var image = new Image<Rgba32>(6, 4);
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }
}
