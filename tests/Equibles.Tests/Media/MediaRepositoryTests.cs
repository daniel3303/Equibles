using Equibles.Data;
using Equibles.Media.Data;
using Equibles.Media.Data.Models;
using Equibles.Media.Repositories;
using Equibles.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using File = Equibles.Media.Data.Models.File;

namespace Equibles.Tests.Media;

public class MediaRepositoryTests : IDisposable {
    private readonly EquiblesDbContext _dbContext;
    private readonly ImageRepository _imageRepo;
    private readonly FileRepository _fileRepo;

    public MediaRepositoryTests() {
        _dbContext = TestDbContextFactory.Create(new MediaModuleConfiguration());
        _imageRepo = new ImageRepository(_dbContext);
        _fileRepo = new FileRepository(_dbContext);
    }

    public void Dispose() {
        _dbContext.Dispose();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static File CreateFile(
        string name = "document",
        string extension = "pdf",
        string contentType = "application/pdf",
        long size = 2048,
        byte[] bytes = null
    ) {
        return new File {
            Id = Guid.NewGuid(),
            Name = name,
            Extension = extension,
            ContentType = contentType,
            Size = size,
            FileContent = new FileContent { Bytes = bytes ?? [0x25, 0x50, 0x44, 0x46] },
        };
    }

    private static Image CreateImage(
        string name = "logo",
        string extension = "png",
        int width = 800,
        int height = 600,
        long size = 4096,
        byte[] bytes = null
    ) {
        return new Image {
            Id = Guid.NewGuid(),
            Name = name,
            Extension = extension,
            ContentType = $"image/{extension}",
            Size = size,
            Width = width,
            Height = height,
            FileContent = new FileContent { Bytes = bytes ?? [0x89, 0x50, 0x4E, 0x47] },
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // FileRepository
    // ═══════════════════════════════════════════════════════════════════

    // ── Add ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task File_Add_PersistsWithAllProperties() {
        var file = CreateFile("report", "html", "text/html", 1024);

        _fileRepo.Add(file);
        await _fileRepo.SaveChanges();

        var result = await _fileRepo.Get(file.Id);
        result.Should().NotBeNull();
        result.Name.Should().Be("report");
        result.Extension.Should().Be("html");
        result.ContentType.Should().Be("text/html");
        result.Size.Should().Be(1024);
    }

    [Fact]
    public async Task File_Add_PersistsFileContent() {
        var bytes = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var file = CreateFile(bytes: bytes);

        _fileRepo.Add(file);
        await _fileRepo.SaveChanges();

        _fileRepo.ClearChangeTracker();
        var result = await _dbContext.Set<FileContent>().FirstOrDefaultAsync(fc => fc.FileId == file.Id);
        result.Should().NotBeNull();
        result.Bytes.Should().BeEquivalentTo(bytes);
    }

    [Fact]
    public async Task File_NameWithExtension_ReturnsCorrectFormat() {
        var file = CreateFile("report", "pdf");

        file.NameWithExtension.Should().Be("report.pdf");
    }

    // ── GetAll ──────────────────────────────────────────────────────────

    [Fact]
    public async Task File_GetAll_ReturnsAllFiles() {
        _fileRepo.Add(CreateFile("file1", "txt"));
        _fileRepo.Add(CreateFile("file2", "csv"));
        _fileRepo.Add(CreateFile("file3", "json"));
        await _fileRepo.SaveChanges();

        var result = await _fileRepo.GetAll().ToListAsync();

        result.Should().HaveCount(3);
    }

    [Fact]
    public void File_GetAll_EmptyTable_ReturnsEmpty() {
        var result = _fileRepo.GetAll();

        result.Should().BeEmpty();
    }

    // ── Get ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task File_Get_ExistingId_ReturnsFile() {
        var file = CreateFile();
        _fileRepo.Add(file);
        await _fileRepo.SaveChanges();

        var result = await _fileRepo.Get(file.Id);

        result.Should().NotBeNull();
        result.Id.Should().Be(file.Id);
    }

    [Fact]
    public async Task File_Get_NonExistentId_ReturnsNull() {
        var result = await _fileRepo.Get(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task File_Update_PersistsChanges() {
        var file = CreateFile("original", "txt");
        _fileRepo.Add(file);
        await _fileRepo.SaveChanges();

        file.Name = "renamed";
        file.Extension = "md";
        _fileRepo.Update(file);
        await _fileRepo.SaveChanges();

        _fileRepo.ClearChangeTracker();
        var result = await _fileRepo.Get(file.Id);
        result.Name.Should().Be("renamed");
        result.Extension.Should().Be("md");
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task File_Delete_RemovesFile() {
        var file = CreateFile();
        _fileRepo.Add(file);
        await _fileRepo.SaveChanges();

        _fileRepo.Delete(file);
        await _fileRepo.SaveChanges();

        var result = await _fileRepo.Get(file.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task File_Delete_DoesNotAffectOtherFiles() {
        var file1 = CreateFile("keep", "txt");
        var file2 = CreateFile("remove", "txt");
        _fileRepo.Add(file1);
        _fileRepo.Add(file2);
        await _fileRepo.SaveChanges();

        _fileRepo.Delete(file2);
        await _fileRepo.SaveChanges();

        var remaining = await _fileRepo.GetAll().ToListAsync();
        remaining.Should().ContainSingle()
            .Which.Name.Should().Be("keep");
    }

    // ── AddRange ────────────────────────────────────────────────────────

    [Fact]
    public async Task File_AddRange_PersistsMultipleFiles() {
        var files = new[] {
            CreateFile("a", "txt"),
            CreateFile("b", "csv"),
            CreateFile("c", "json"),
        };

        _fileRepo.AddRange(files);
        await _fileRepo.SaveChanges();

        _fileRepo.GetAll().Should().HaveCount(3);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ImageRepository
    // ═══════════════════════════════════════════════════════════════════

    // ── Add ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Image_Add_PersistsWithDimensions() {
        var image = CreateImage("banner", "jpg", 1920, 1080, 8192);

        _imageRepo.Add(image);
        await _imageRepo.SaveChanges();

        var result = await _imageRepo.Get(image.Id);
        result.Should().NotBeNull();
        result.Name.Should().Be("banner");
        result.Extension.Should().Be("jpg");
        result.Width.Should().Be(1920);
        result.Height.Should().Be(1080);
        result.Size.Should().Be(8192);
        result.ContentType.Should().Be("image/jpg");
    }

    [Fact]
    public async Task Image_Add_PersistsFileContent() {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var image = CreateImage(bytes: bytes);

        _imageRepo.Add(image);
        await _imageRepo.SaveChanges();

        _imageRepo.ClearChangeTracker();
        var content = await _dbContext.Set<FileContent>().FirstOrDefaultAsync(fc => fc.FileId == image.Id);
        content.Should().NotBeNull();
        content.Bytes.Should().BeEquivalentTo(bytes);
    }

    // ── GetAll ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Image_GetAll_ReturnsAllImages() {
        _imageRepo.Add(CreateImage("img1", "png", 100, 100));
        _imageRepo.Add(CreateImage("img2", "jpg", 200, 200));
        await _imageRepo.SaveChanges();

        var result = await _imageRepo.GetAll().ToListAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public void Image_GetAll_EmptyTable_ReturnsEmpty() {
        var result = _imageRepo.GetAll();

        result.Should().BeEmpty();
    }

    // ── Get ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Image_Get_ExistingId_ReturnsImage() {
        var image = CreateImage();
        _imageRepo.Add(image);
        await _imageRepo.SaveChanges();

        var result = await _imageRepo.Get(image.Id);

        result.Should().NotBeNull();
        result.Id.Should().Be(image.Id);
    }

    [Fact]
    public async Task Image_Get_NonExistentId_ReturnsNull() {
        var result = await _imageRepo.Get(Guid.NewGuid());

        result.Should().BeNull();
    }

    // ── Update ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Image_Update_PersistsDimensionChanges() {
        var image = CreateImage("photo", "png", 800, 600);
        _imageRepo.Add(image);
        await _imageRepo.SaveChanges();

        image.Width = 1600;
        image.Height = 1200;
        _imageRepo.Update(image);
        await _imageRepo.SaveChanges();

        _imageRepo.ClearChangeTracker();
        var result = await _imageRepo.Get(image.Id);
        result.Width.Should().Be(1600);
        result.Height.Should().Be(1200);
    }

    // ── Delete ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Image_Delete_RemovesImage() {
        var image = CreateImage();
        _imageRepo.Add(image);
        await _imageRepo.SaveChanges();

        _imageRepo.Delete(image);
        await _imageRepo.SaveChanges();

        var result = await _imageRepo.Get(image.Id);
        result.Should().BeNull();
    }

    // ── Image inherits File properties ──────────────────────────────────

    [Fact]
    public void Image_NameWithExtension_InheritsFromFile() {
        var image = CreateImage("avatar", "webp");

        image.NameWithExtension.Should().Be("avatar.webp");
    }

    [Fact]
    public async Task Image_InheritsFileBaseProperties() {
        var image = CreateImage("chart", "svg", 400, 300);
        image.ContentType = "image/svg+xml";
        image.Size = 12345;

        _imageRepo.Add(image);
        await _imageRepo.SaveChanges();

        _imageRepo.ClearChangeTracker();
        var result = await _imageRepo.Get(image.Id);
        result.Name.Should().Be("chart");
        result.Extension.Should().Be("svg");
        result.ContentType.Should().Be("image/svg+xml");
        result.Size.Should().Be(12345);
        result.Width.Should().Be(400);
        result.Height.Should().Be(300);
    }
}
