using Equibles.IntegrationTests.Helpers;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Repositories;

namespace Equibles.IntegrationTests.Media;

public class FileManagerTests
{
    private readonly FileManager _sut;
    private readonly FileRepository _repository;

    public FileManagerTests()
    {
        var context = TestDbContextFactory.Create(new MediaModuleConfiguration());
        _repository = new FileRepository(context);
        _sut = new FileManager(_repository);
    }

    [Fact]
    public async Task SaveFile_Pdf_SetsCorrectExtensionAndMime()
    {
        var file = await _sut.SaveFile([0x01, 0x02], "document.pdf");

        file.Extension.Should().Be("pdf");
        file.ContentType.Should().Be("application/pdf");
        file.Name.Should().Be("document");
    }

    [Fact]
    public async Task SaveFile_Jpg_SetsImageJpegMime()
    {
        var file = await _sut.SaveFile([0x01], "photo.jpg");

        file.Extension.Should().Be("jpg");
        file.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task SaveFile_Png_SetsImagePngMime()
    {
        var file = await _sut.SaveFile([0x01], "image.png");

        file.Extension.Should().Be("png");
        file.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task SaveFile_Txt_SetsTextPlainMime()
    {
        var file = await _sut.SaveFile([0x01], "readme.txt");

        file.Extension.Should().Be("txt");
        file.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task SaveFile_AllowlistedExtensionWithNoSpecificMime_UsesOctetStream()
    {
        // SaveFile derives Content-Type from MimeTypeMap.GetMimeType(extension).
        // Most allowlisted types (.pdf, .jpg, .png, .txt, .doc…) map to a
        // specific MIME, but `.psd` has no specific mapping and resolves to
        // application/octet-stream. This pins the safe-content-type behaviour
        // for an allowlisted-but-unmapped extension so the column is never
        // null/empty (which would 500 or content-sniff when served back).
        // Note: SaveFile now enforces the AcceptedExtensions allowlist
        // (GH-766), so a genuinely unknown extension is rejected outright —
        // this exercises the octet-stream path within that contract using a
        // permitted extension.
        var file = await _sut.SaveFile([0x38, 0x42, 0x50, 0x53], "layers.psd");

        file.Extension.Should().Be("psd");
        file.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task SaveFile_NoExtension_ThrowsArgumentException()
    {
        var act = () => _sut.SaveFile([0x01], "filename");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*extension*");
    }

    [Fact]
    public async Task SaveFile_SizeMatchesContentLength()
    {
        var content = new byte[42];

        var file = await _sut.SaveFile(content, "data.pdf");

        file.Size.Should().Be(42);
    }

    [Fact]
    public async Task SaveFile_FileContentLinked()
    {
        var content = new byte[] { 1, 2, 3 };

        var file = await _sut.SaveFile(content, "test.pdf");

        file.FileContent.Should().NotBeNull();
        file.FileContent.Bytes.Should().BeEquivalentTo(content);
        file.FileContent.File.Should().BeSameAs(file);
    }

    [Fact]
    public async Task SaveFile_EntityAddedToRepository()
    {
        await _sut.SaveFile([0x01], "test.pdf");
        await _repository.SaveChanges();

        _repository.GetAll().Should().HaveCount(1);
    }

    [Fact]
    public void DeleteFile_Null_DoesNotThrow()
    {
        var act = () => _sut.DeleteFile(null);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task DeleteFile_SavedFile_RemovesItFromRepository()
    {
        // The companion DeleteFile_Null test only exercises the early
        // null guard. Without this, the actual delete branch is unpinned —
        // a refactor that "simplifies" DeleteFile to a no-op (or
        // mis-routes it to a different repository) would leak files and
        // the existing tests wouldn't catch it. Round-trip through
        // SaveFile → SaveChanges → DeleteFile → SaveChanges to assert
        // the file is actually removed.
        var file = await _sut.SaveFile([0x01, 0x02], "to-delete.pdf");
        await _repository.SaveChanges();
        _repository.GetAll().Should().ContainSingle();

        _sut.DeleteFile(file);
        await _repository.SaveChanges();

        _repository.GetAll().Should().BeEmpty();
    }
}
