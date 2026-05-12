using System.Globalization;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class DocumentTypeConverterTests {
    private readonly DocumentTypeConverter _sut = new();

    [Fact]
    public void ConvertFrom_UnknownStringValue_ThrowsFormatException() {
        var act = () => _sut.ConvertFrom(null, CultureInfo.InvariantCulture, "NotARealDocumentType");

        act.Should().Throw<FormatException>()
            .WithMessage("*NotARealDocumentType*");
    }
}
