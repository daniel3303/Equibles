using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientSplitCsvLineLeadingSpaceTests
{
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient).GetMethod(
        "SplitCsvLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // Contract: "quoted content is taken verbatim" — a field's value is what is
    // *inside* the quotes. A space between the delimiter and the opening quote
    // (`, "b"`, ubiquitous in CFTC CSVs) is not part of the quoted content, so
    // the parsed value must be "b", not " b".
    [Fact]
    public void SplitCsvLine_SpaceBeforeOpeningQuote_DoesNotLeakSpaceIntoQuotedValue()
    {
        var line = "a, \"b\"";

        var fields = (string[])SplitCsvLineMethod.Invoke(null, [line]);

        fields.Should().Equal("a", "b");
    }
}
