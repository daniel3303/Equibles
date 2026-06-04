using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientSplitCsvLineQuotedWhitespaceTests
{
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient).GetMethod(
        "SplitCsvLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // Contract: "Quoted content is taken verbatim; unquoted fields are
    // whitespace-trimmed." Existing pins exercise quoted values that contain no
    // distinguishing whitespace, so none prove the verbatim half against the trim
    // half. A quoted field that is ONLY spaces must survive intact ("   "), not
    // collapse to "" — a refactor that trimmed every field uniformly would pass all
    // current tests but silently destroy quoted whitespace.
    [Fact]
    public void SplitCsvLine_QuotedWhitespaceOnlyField_IsPreservedVerbatimNotTrimmed()
    {
        var line = "a,\"   \",b";

        var fields = (string[])SplitCsvLineMethod.Invoke(null, [line]);

        fields.Should().Equal("a", "   ", "b");
    }
}
