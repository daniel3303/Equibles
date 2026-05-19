using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserParseSecDateTests
{
    // Adversarial: the XML-doc contract states cover-page dates are MM-DD-YYYY.
    // "02-03-2024" is the ambiguous case — under the documented US ordering it is
    // February 3 2024, NOT March 2. A caller relies on this ordering; a regression
    // to dd-MM-yyyy (or a culture-dependent fallback) would silently swap every
    // such date. Pin the month/day ordering against that.
    [Fact]
    public void ParseSecDate_AmbiguousMonthDay_UsesDocumentedUsOrdering()
    {
        var parse = typeof(Filing13FXmlParser).GetMethod(
            "ParseSecDate",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (DateOnly)parse.Invoke(null, ["02-03-2024"])!;

        result.Should().Be(new DateOnly(2024, 2, 3));
    }
}
