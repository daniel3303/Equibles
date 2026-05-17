using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientSplitCsvLineEmbeddedCommaTests
{
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient).GetMethod(
        "SplitCsvLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void SplitCsvLine_CommaInsideQuotedField_IsTreatedAsValueNotDelimiter()
    {
        // Contract (doc-comment: comma-separated values with quoted fields,
        // RFC 4180): a comma inside a quoted field is part of the value, not a
        // field separator. The existing pins cover escaped quotes and leading
        // padding but never a quoted comma — the defining property of CSV.
        var line = "123, \"Hello, World\" ,456";

        var fields = (string[])SplitCsvLineMethod.Invoke(null, [line]);

        fields.Should().Equal("123", "Hello, World", "456");
    }
}
