using System.Globalization;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class DocumentTypeConverterConvertToNonStringTests
{
    private readonly DocumentTypeConverter _sut = new();

    [Fact]
    public void ConvertTo_NonStringDestination_ThrowsNotSupportedException()
    {
        // Contract: ConvertTo only knows how to emit the stable Value string from a
        // DocumentType — its `if` guards on `destinationType == typeof(string)`. Any other
        // destination must fall through to `base.ConvertTo`, which per the TypeConverter
        // convention rejects an unsupported target with NotSupportedException. This pins the
        // RIGHT arm of that guard (the existing ConvertTo pin only exercises the string arm).
        // A refactor that dropped the base delegation (e.g. returning Value unconditionally,
        // or `documentType.Value` regardless of destinationType) would silently hand back a
        // string where the caller asked for an int — caught here because typeof(int) must
        // throw rather than coerce.
        var act = () =>
            _sut.ConvertTo(null, CultureInfo.InvariantCulture, DocumentType.TenK, typeof(int));

        act.Should().Throw<NotSupportedException>();
    }
}
