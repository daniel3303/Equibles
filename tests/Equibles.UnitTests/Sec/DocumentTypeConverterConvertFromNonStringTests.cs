using System.Globalization;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Sec;

public class DocumentTypeConverterConvertFromNonStringTests
{
    private readonly DocumentTypeConverter _sut = new();

    [Fact]
    public void ConvertFrom_NonStringValue_ThrowsNotSupportedException()
    {
        // Contract: ConvertFrom only handles the explicit `value is string` arm; any other
        // source must fall through to `base.ConvertFrom`, which per the TypeConverter
        // convention rejects an unsupported source with NotSupportedException. The sibling
        // `CanConvertFrom(typeof(int)) == false` pins the GATE; this pins the actual
        // rejection. A refactor that dropped the base call (e.g. returning null, or coercing
        // the value via ToString before FromValue) would silently admit numeric route values
        // that DocumentType.FromValue never recognizes — caught here because int now throws
        // NotSupportedException rather than returning a bogus or null DocumentType.
        var act = () => _sut.ConvertFrom(null, CultureInfo.InvariantCulture, 123);

        act.Should().Throw<NotSupportedException>();
    }
}
