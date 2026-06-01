using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the lone-CR record terminator the tokenizer documents but no existing test exercises:
/// "Swallow the LF of a CRLF pair; a lone CR also ends the record." The existing pins cover LF
/// and CRLF endings only, so the false arm of the CRLF look-ahead (a '\r' NOT followed by '\n',
/// i.e. classic-Mac / mixed line endings) is unproven. Oracle derived from the documented
/// contract before reading the switch body: each lone '\r' must close the current record exactly
/// as '\n' does, yielding one record per line with no spurious empty trailing record. A
/// regression that dropped the '\r' case — letting carriage returns fall through to the default
/// arm and append into the field — would collapse this into a single record and fail loudly.
/// </summary>
public class CsvRecordReaderLoneCarriageReturnTests
{
    [Fact]
    public void Read_LoneCarriageReturnLineEndings_SplitsOneRecordPerLine()
    {
        var records = CsvRecordReader.Read(new StringReader("a,b\rc,d\r")).ToList();

        records.Should().HaveCount(2);
        records[0].Should().Equal("a", "b");
        records[1].Should().Equal("c", "d");
    }
}
