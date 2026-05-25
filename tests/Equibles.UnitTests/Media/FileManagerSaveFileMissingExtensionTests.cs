using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Repositories;
using NSubstitute;

namespace Equibles.UnitTests.Media;

// Lane A (adversarial): SaveFile's contract says "The file name is used to
// infer the file extension." A filename with no extension (no dot) must
// throw ArgumentException — silently accepting it would store a file with
// a null extension that can't be served with a correct Content-Type.
public class FileManagerSaveFileMissingExtensionTests
{
    [Fact]
    public async Task SaveFile_FilenameWithoutExtension_ThrowsArgumentException()
    {
        var repository = Substitute.For<FileRepository>((EquiblesDbContext)null);
        var sut = new FileManager(repository);

        var act = () => sut.SaveFile("content"u8.ToArray(), "README");

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*extension*");
    }
}
