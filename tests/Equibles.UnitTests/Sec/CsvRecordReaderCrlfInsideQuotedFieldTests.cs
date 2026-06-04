using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

public class CsvRecordReaderCrlfInsideQuotedFieldTests
{
    // Contract: a line break inside a quoted field is preserved and does not end
    // the record. The existing newline-inside pin uses a bare \n; a CRLF inside
    // quotes must be kept verbatim (\r\n) — the CR/LF swallowing that collapses
    // CRLF between records only applies OUTSIDE quotes, so a quoted CRLF must not
    // be normalized or split the record.
    [Fact]
    public void Read_CrlfInsideQuotedField_PreservesCrlfAndKeepsOneRecord()
    {
        var records = CsvRecordReader
            .Read(new StringReader("\"line1\r\nline2\",second\n"))
            .ToList();

        records.Should().HaveCount(1);
        records[0].Should().Equal("line1\r\nline2", "second");
    }
}
