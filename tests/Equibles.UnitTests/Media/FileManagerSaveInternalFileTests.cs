using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using NSubstitute;

namespace Equibles.UnitTests.Media;

// Lane B (coverage): SaveInternalFile was entirely zero-hit. Its documented job
// is to persist a trusted, system-generated blob with an explicit extension and
// content type, BYPASSING the SaveFile upload allowlist. Pins that an extension
// absent from AcceptedExtensions ("gz") is accepted verbatim (no exception) and
// the File entity is assembled from the explicit arguments — a regression that
// re-applied the allowlist here would break SEC XBRL envelope ingest.
public class FileManagerSaveInternalFileTests
{
    [Fact]
    public async Task SaveInternalFile_NonAllowlistedExtension_PersistsBlobVerbatim()
    {
        var repository = Substitute.For<FileRepository>((EquiblesFinancialDbContext)null);
        var sut = new FileManager(repository);
        var content = "gzip-xbrl-bytes"u8.ToArray();

        var file = await sut.SaveInternalFile(content, "xbrl-envelope", "gz", "application/gzip");

        file.Extension.Should().Be("gz");
        file.Name.Should().Be("xbrl-envelope");
        file.ContentType.Should().Be("application/gzip");
        file.Size.Should().Be(content.Length);
        file.FileContent.Bytes.Should().BeEquivalentTo(content);
        repository.Received(1).Add(file);
    }
}
