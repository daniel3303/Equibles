namespace Equibles.Tests.Models;

public class FileModelTests {
    [Fact]
    public void NameWithExtension_CombinesNameAndExtension() {
        var file = new Equibles.Media.Data.Models.File {
            Name = "document",
            Extension = "pdf"
        };

        file.NameWithExtension.Should().Be("document.pdf");
    }

    [Fact]
    public void NameWithExtension_HandlesMultiPartName() {
        var file = new Equibles.Media.Data.Models.File {
            Name = "my.report.final",
            Extension = "xlsx"
        };

        file.NameWithExtension.Should().Be("my.report.final.xlsx");
    }

    [Fact]
    public void Id_DefaultsToUniqueGuid() {
        var file1 = new Equibles.Media.Data.Models.File();
        var file2 = new Equibles.Media.Data.Models.File();

        file1.Id.Should().NotBe(Guid.Empty);
        file2.Id.Should().NotBe(Guid.Empty);
        file1.Id.Should().NotBe(file2.Id);
    }

    [Fact]
    public void Size_DefaultsToZero() {
        var file = new Equibles.Media.Data.Models.File();

        file.Size.Should().Be(0);
    }
}
