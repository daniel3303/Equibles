using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientSplitCsvLineUnterminatedQuoteTests
{
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient).GetMethod(
        "SplitCsvLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void SplitCsvLine_UnterminatedQuotedField_CapturesContentWithoutThrowing()
    {
        // Contract (doc-comment: RFC 4180-style splitter for CFTC files):
        // RFC 4180 doesn't promise recovery from a missing closing quote, but
        // the importer is a defense-in-depth scrape — a malformed line must
        // not throw and abort the whole yearly file. The existing pins cover
        // four well-formed corner cases (embedded comma, escaped quote,
        // leading space, trailing junk); this completes the suite with the
        // canonical malformed shape. A regression that threw
        // "unterminated quote" or omitted the trailing field would crash
        // every yearly load on the first stray quote in a contract name.
        var line = "\"hello";

        var fields = (string[])SplitCsvLineMethod.Invoke(null, [line]);

        fields.Should().ContainSingle().Which.Should().Be("hello");
    }
}
