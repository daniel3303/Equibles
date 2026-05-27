using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using NSubstitute;

namespace Equibles.UnitTests.Media;

// Lane A (adversarial): SaveFile's allowlist must reject non-accepted
// extensions. A .exe payload must throw ArgumentException — accepting it
// would let an attacker persist and serve an executable.
public class FileManagerSaveFileRejectedExtensionTests
{
    [Fact]
    public async Task SaveFile_DisallowedExtension_ThrowsArgumentException()
    {
        var repository = Substitute.For<FileRepository>((EquiblesFinancialDbContext)null);
        var sut = new FileManager(repository);

        var act = () => sut.SaveFile("malicious"u8.ToArray(), "payload.exe");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*extension*not allowed*");
    }
}
