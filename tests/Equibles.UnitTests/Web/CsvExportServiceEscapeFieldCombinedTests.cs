using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class CsvExportServiceEscapeFieldCombinedTests
{
    // EscapeField's sibling pins each isolate a single special character:
    // ContainsComma → wraps; ContainsDoubleQuote → doubles + wraps;
    // ContainsNewline → wraps; ContainsCarriageReturn → wraps. Each pin
    // exercises one branch of the IndexOfAny(['"', ',', '\n', '\r'])
    // detection and one shape of escaping.
    //
    // The interesting risk lives in the *combination*: a field that
    // contains BOTH a double-quote AND a delimiter (comma / newline / CR).
    // The two escaping rules layer:
    //   1. Inner quotes must be doubled ("" -> """")
    //   2. The whole field is then wrapped in outer quotes
    //
    // A refactor that handled each special character as an independent
    // branch (e.g. an if-else cascade rather than the current "doubled +
    // wrapped" pipeline) would compile, pass every sibling pin (each
    // isolates one character), and silently emit invalid CSV for any
    // field that combines them — every quoted free-form string with
    // an embedded comma. Excel / pandas / RFC-4180 parsers would
    // misalign columns starting at that field.
    //
    // Pin: a field that combines both forces the full pipeline. The
    // expected output is byte-exact per RFC-4180:
    //   input  : He said, "hi"
    //   output : "He said, ""hi"""
    [Fact]
    public void EscapeField_ContainsBothCommaAndDoubleQuote_DoublesInnerQuotesAndWrapsWholeField()
    {
        var result = CsvExportService.EscapeField("He said, \"hi\"");

        result.Should().Be("\"He said, \"\"hi\"\"\"");
    }
}
