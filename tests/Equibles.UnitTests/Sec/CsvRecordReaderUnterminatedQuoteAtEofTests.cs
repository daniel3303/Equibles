using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins EOF reached while still inside a quoted field — the one tokenizer path the existing
/// pins never reach (every other case closes its quote before the stream ends). The reader is
/// built to stream a 14&#160;MB download one record at a time, so a connection truncated mid-quote
/// is a real failure mode: the partial final field must still surface as the last record (data
/// not silently dropped), and its embedded comma must stay literal — the field is quoted, so the
/// comma is content, not a delimiter. A "simplification" that discarded the unterminated field, or
/// recovered by re-tokenizing the tail as unquoted (splitting "a,b" into two fields), breaks both.
/// </summary>
public class CsvRecordReaderUnterminatedQuoteAtEofTests
{
    [Fact]
    public void Read_UnterminatedQuotedFieldAtEof_EmitsAccumulatedFieldWithCommaPreserved()
    {
        var records = CsvRecordReader.Read(new StringReader("\"a,b")).ToList();

        records.Should().HaveCount(1);
        records[0].Should().Equal("a,b");
    }
}
