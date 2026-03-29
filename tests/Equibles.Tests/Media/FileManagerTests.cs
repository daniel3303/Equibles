using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Repositories;
using Equibles.Tests.Helpers;

namespace Equibles.Tests.Media;

public class FileManagerTests {
    private readonly FileManager _sut;
    private readonly FileRepository _repository;

    public FileManagerTests() {
        var context = TestDbContextFactory.Create(new MediaModuleConfiguration());
        _repository = new FileRepository(context);
        _sut = new FileManager(_repository);
    }

    [Fact]
    public async Task SaveFile_Pdf_SetsCorrectExtensionAndMime() {
        var file = await _sut.SaveFile([0x01, 0x02], "document.pdf");

        file.Extension.Should().Be("pdf");
        file.ContentType.Should().Be("application/pdf");
        file.Name.Should().Be("document");
    }

    [Fact]
    public async Task SaveFile_Jpg_SetsImageJpegMime() {
        var file = await _sut.SaveFile([0x01], "photo.jpg");

        file.Extension.Should().Be("jpg");
        file.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task SaveFile_Png_SetsImagePngMime() {
        var file = await _sut.SaveFile([0x01], "image.png");

        file.Extension.Should().Be("png");
        file.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task SaveFile_Txt_SetsTextPlainMime() {
        var file = await _sut.SaveFile([0x01], "readme.txt");

        file.Extension.Should().Be("txt");
        file.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task SaveFile_NoExtension_ThrowsArgumentException() {
        var act = () => _sut.SaveFile([0x01], "filename");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*extension*");
    }

    [Fact]
    public async Task SaveFile_SizeMatchesContentLength() {
        var content = new byte[42];

        var file = await _sut.SaveFile(content, "data.pdf");

        file.Size.Should().Be(42);
    }

    [Fact]
    public async Task SaveFile_FileContentLinked() {
        var content = new byte[] { 1, 2, 3 };

        var file = await _sut.SaveFile(content, "test.pdf");

        file.FileContent.Should().NotBeNull();
        file.FileContent.Bytes.Should().BeEquivalentTo(content);
        file.FileContent.File.Should().BeSameAs(file);
    }

    [Fact]
    public async Task SaveFile_EntityAddedToRepository() {
        await _sut.SaveFile([0x01], "test.pdf");
        await _repository.SaveChanges();

        _repository.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void DeleteFile_Null_DoesNotThrow() {
        var act = () => _sut.DeleteFile(null);

        act.Should().NotThrow();
    }
}
