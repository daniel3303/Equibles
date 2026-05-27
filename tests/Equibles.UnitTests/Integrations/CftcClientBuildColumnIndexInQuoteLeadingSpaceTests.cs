using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientBuildColumnIndexInQuoteLeadingSpaceTests
{
    private static readonly MethodInfo BuildColumnIndexMethod = typeof(CftcClient).GetMethod(
        "BuildColumnIndex",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // GH-2159 fix added a trailing `.Trim()` after `.Trim('"')` so that
    // CFTC's in-quote leading-space header (`" Total Reportable
    // Positions-Long (All)"` in deacot{year}.zip — a known
    // source-data quirk) normalises to the canonical
    // `Total Reportable Positions-Long (All)` lookup key. The
    // existing `BuildColumnIndex_QuotedHeaderName_IndexedWithoutQuotes`
    // sibling pins quote-stripping on a header with NO in-quote
    // whitespace, so dropping the final `.Trim()` would re-introduce
    // the leading-space prefix on the dict key, every `Get(...)`
    // for that column would miss, and Total-Reportable numeric
    // fields would silently parse to null on every CFTC import
    // cycle — the exact failure mode of GH-2159.
    [Fact]
    public void BuildColumnIndex_QuotedHeaderWithInQuoteLeadingSpace_IndexedUnderTrimmedCanonicalKey()
    {
        var headerLine = "\" Total Reportable Positions-Long (All)\",\"Other Column\"";

        var index = (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);

        index
            .Should()
            .ContainKey(
                "Total Reportable Positions-Long (All)",
                "CFTC ships this column with an in-quote leading space; BuildColumnIndex must trim after the quote-strip so production lookups by the canonical name resolve"
            )
            .WhoseValue.Should()
            .Be(0);
        // Negative pin: the un-trimmed key form (with leading space) must NOT
        // be in the index — a regression that dropped the trailing .Trim()
        // would re-introduce it here.
        index.Should().NotContainKey(" Total Reportable Positions-Long (All)");
    }
}
