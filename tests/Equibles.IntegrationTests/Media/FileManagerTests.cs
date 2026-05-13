using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Media.Repositories;
using Equibles.IntegrationTests.Helpers;

namespace Equibles.IntegrationTests.Media;

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
    public async Task SaveFile_UnknownExtension_FallsBackToApplicationOctetStream() {
        // SaveFile derives Content-Type from MimeTypeMap.GetMimeType(extension).
        // Every existing test (.pdf, .jpg, .png, .txt) hits a mapped entry, so
        // the fallback path — `if (string.IsNullOrEmpty(contentType))
        // contentType = "application/octet-stream";` — is unexercised. That
        // fallback is the only thing that prevents an unknown extension
        // (e.g. SEC paper-filing artifacts with bespoke suffixes like
        // `.xbrl.zip.gz`, partner uploads with obsolete suffixes, anything
        // outside the curated AcceptedExtensions list) from persisting a
        // null or empty `ContentType` column — which downstream blows up
        // when the file is served back to the browser (the response either
        // 500s on null header, or sniffs into something dangerous like
        // text/html). A refactor that drops the fallback (e.g. assuming
        // MimeTypeMap always returns non-empty, which it doesn't for
        // unknown suffixes) would compile cleanly and pass every existing
        // test, then silently corrupt the file metadata on the next bespoke
        // upload. Pin the fallback so the regression surfaces here.
        var file = await _sut.SaveFile([0x01, 0x02, 0x03], "weird.zzz");

        file.Extension.Should().Be("zzz");
        file.ContentType.Should().Be("application/octet-stream");
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

    [Fact]
    public async Task DeleteFile_SavedFile_RemovesItFromRepository() {
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
