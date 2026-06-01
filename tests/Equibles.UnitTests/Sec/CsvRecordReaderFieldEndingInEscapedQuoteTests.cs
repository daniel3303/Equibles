using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the trickiest RFC-4180 branch the tokenizer claims to support: a doubled quote ("")
/// immediately adjacent to the field's closing quote, producing a run of three quotes ("""). The
/// existing tests only place doubled quotes mid-field (always followed by a non-quote), so the
/// peek-ahead path where an escaped quote butts up against the terminator is otherwise unexercised.
/// Per RFC 4180 the field value is the text with a single trailing literal quote.
/// </summary>
public class CsvRecordReaderFieldEndingInEscapedQuoteTests
{
    [Fact]
    public void Read_QuotedFieldEndingInEscapedQuote_PreservesTrailingLiteralQuote()
    {
        var records = CsvRecordReader
            .Read(new StringReader("\"ends with quote\"\"\",next\n"))
            .ToList();

        records.Should().HaveCount(1);
        records[0].Should().Equal("ends with quote\"", "next");
    }
}
