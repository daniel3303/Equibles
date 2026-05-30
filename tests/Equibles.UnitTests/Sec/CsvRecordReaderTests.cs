using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Direct pins for the RFC-4180 cases the Form ADV cassette does not exercise: a doubled quote
/// ("") as a literal quote, commas and line breaks inside a quoted field, CRLF line endings, and
/// a final record with no trailing newline. These are the subtle branches of the hand-rolled
/// tokenizer most likely to be broken by a later "simplification".
/// </summary>
public class CsvRecordReaderTests
{
    private static List<List<string>> Read(string csv) =>
        CsvRecordReader.Read(new StringReader(csv)).ToList();

    [Fact]
    public void Read_QuotedFieldWithCommaAndDoubledQuote_KeepsFieldIntact()
    {
        var records = Read("a,\"x, \"\"y\"\" z\",c\n");

        records.Should().HaveCount(1);
        records[0].Should().Equal("a", "x, \"y\" z", "c");
    }

    [Fact]
    public void Read_NewlineInsideQuotedField_DoesNotSplitTheRecord()
    {
        var records = Read("\"line1\nline2\",second\n");

        records.Should().HaveCount(1);
        records[0].Should().Equal("line1\nline2", "second");
    }

    [Fact]
    public void Read_CrlfLineEndings_SplitsOneRecordPerLine()
    {
        var records = Read("h1,h2\r\nv1,v2\r\n");

        records.Should().HaveCount(2);
        records[0].Should().Equal("h1", "h2");
        records[1].Should().Equal("v1", "v2");
    }

    [Fact]
    public void Read_LastRecordWithoutTrailingNewline_IsStillEmitted()
    {
        var records = Read("h1,h2\nv1,v2");

        records.Should().HaveCount(2);
        records[1].Should().Equal("v1", "v2");
    }

    [Fact]
    public void Read_EmptyInput_YieldsNothing()
    {
        Read(string.Empty).Should().BeEmpty();
    }
}
