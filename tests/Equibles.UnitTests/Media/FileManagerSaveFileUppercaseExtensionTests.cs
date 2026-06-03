using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using NSubstitute;

namespace Equibles.UnitTests.Media;

// Lane A (adversarial): SaveFile documents its allowlist as case-insensitive,
// and its summary says the content type is inferred from the extension. So an
// accepted extension supplied in upper case (e.g. "report.PDF") must be both
// accepted and resolved to the real MIME type — not rejected, and not silently
// downgraded to "application/octet-stream". A case-sensitive allowlist or MIME
// lookup would break that promise; only lower-case input is exercised elsewhere.
public class FileManagerSaveFileUppercaseExtensionTests
{
    [Fact]
    public async Task SaveFile_AcceptedExtensionInUpperCase_ResolvesRealContentType()
    {
        var repository = Substitute.For<FileRepository>((EquiblesFinancialDbContext)null);
        var sut = new FileManager(repository);
        var content = "fake-pdf-content"u8.ToArray();

        var file = await sut.SaveFile(content, "quarterly-report.PDF");

        file.Extension.Should().Be("PDF");
        file.ContentType.Should().Be("application/pdf");
    }
}
