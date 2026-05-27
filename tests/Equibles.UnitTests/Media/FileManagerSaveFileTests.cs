using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using NSubstitute;

namespace Equibles.UnitTests.Media;

// Lane B (coverage): pins the happy-path of FileManager.SaveFile — extension
// extraction, allowlist gate, MIME-type resolution, and File entity assembly.
// All 27 lines of SaveFile were zero-hit in unit coverage.
public class FileManagerSaveFileTests
{
    private readonly FileManager _sut;
    private readonly FileRepository _repository;

    public FileManagerSaveFileTests()
    {
        _repository = Substitute.For<FileRepository>((EquiblesFinancialDbContext)null);
        _sut = new FileManager(_repository);
    }

    [Fact]
    public async Task SaveFile_ValidPdf_ReturnsFileWithCorrectProperties()
    {
        var content = "fake-pdf-content"u8.ToArray();

        var file = await _sut.SaveFile(content, "quarterly-report.pdf");

        file.Extension.Should().Be("pdf");
        file.Name.Should().Be("quarterly-report");
        file.Size.Should().Be(content.Length);
        file.ContentType.Should().Be("application/pdf");
        file.FileContent.Should().NotBeNull();
        file.FileContent.Bytes.Should().BeEquivalentTo(content);
        _repository.Received(1).Add(file);
    }
}
