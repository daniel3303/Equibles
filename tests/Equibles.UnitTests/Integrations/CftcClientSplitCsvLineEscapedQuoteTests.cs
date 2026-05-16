using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientSplitCsvLineEscapedQuoteTests
{
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient).GetMethod(
        "SplitCsvLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void SplitCsvLine_QuotedFieldWithEscapedQuote_UnescapesToSingleQuote()
    {
        // Contract: a quote-aware CSV splitter follows the universal convention —
        // a doubled "" inside a quoted field is one literal ". The existing pin
        // already requires the split to "honour double quotes".
        var line = "\"a\"\"b\"";

        var fields = (string[])SplitCsvLineMethod.Invoke(null, [line]);

        fields.Should().Equal("a\"b");
    }
}
