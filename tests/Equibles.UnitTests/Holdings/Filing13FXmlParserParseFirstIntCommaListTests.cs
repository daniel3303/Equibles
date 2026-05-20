using System.Reflection;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Filing13FXmlParserParseFirstIntCommaListTests
{
    // Filing13FXmlParser carries TWO sibling parsers that read free-text numerics
    // from cover-page / info-table XML — and they treat the comma OPPOSITELY:
    //   • ParseLong("1,234") → 1234 (comma is a thousand-separator)
    //   • ParseFirstInt("1,234") → 1 (comma is a LIST separator; takes the first)
    // The semantic split is load-bearing: ParseFirstInt is used to pluck the
    // first OTHERMANAGER sequence number from comma-listed OTHERMANAGER fields
    // (e.g. "1,3,5" meaning "managers 1, 3, and 5"). If a refactor "harmonizes"
    // the two methods (intuitive to a contributor seeing two private numeric
    // parsers in the same file), every multi-manager filing would silently
    // misread the sequence-number reference as a single thousand-separated
    // integer, polluting the OtherManagers join.
    //
    // No existing test pins this distinction. Pin "1,234" → 1 specifically:
    // this input is canonical-ambiguous (parses to 1234 under thousand-
    // separator semantics, 1 under list-separator semantics). The assertion
    // catches a refactor that flips the parser to the wrong shape regardless
    // of which direction it flips.
    [Fact]
    public void ParseFirstInt_CommaSeparatedList_ReturnsFirstElementNotThousandSeparated()
    {
        var method = typeof(Filing13FXmlParser).GetMethod(
            "ParseFirstInt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int?)method.Invoke(null, ["1,234"]);

        result.Should().Be(1);
    }
}
