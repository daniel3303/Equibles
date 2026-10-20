using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// SEC's Form ADV filing archive writes quotes inside a quoted field unescaped. Real rows from
/// its Schedule D 7.B.(1) table: each must keep its columns and leave the next row intact.
/// </summary>
public class CsvRecordReaderUnescapedQuoteTests
{
    [Theory]
    [InlineData(
        "788249,\"ORBIMED PRIVATE INVESTMENTS V, LP\",\"FUND MEETS THE DEFINITION OF BOTH \"VENTURE CAPITAL FUND\" AND \"HEDGE FUND\".\",744000000\n",
        "FUND MEETS THE DEFINITION OF BOTH VENTURE CAPITAL FUND\" AND \"HEDGE FUND\".\""
    )]
    [InlineData(
        "788249,\"ORBIMED PRIVATE INVESTMENTS, LP\",\"FUND MEETS THE DEFINITION OF BOTH \"VENTURE CAPITAL FUND\" AND \"HEDGE FUND\"\",6000000\n",
        "FUND MEETS THE DEFINITION OF BOTH VENTURE CAPITAL FUND\" AND \"HEDGE FUND\"\""
    )]
    public void Read_UnescapedQuoteInsideAQuotedField_KeepsTheColumns(string row, string note)
    {
        var records = CsvRecordReader
            .Read(new StringReader(row + "788250,\"NEXT FUND\",\"\",5\n"))
            .ToList();

        records.Should().HaveCount(2);
        records[0].Should().HaveCount(4);
        records[0][2].Should().Be(note);
        records[1].Should().Equal("788250", "NEXT FUND", "", "5");
    }

    [Fact]
    public void Read_QuoteInsideAnUnquotedField_IsLiteral()
    {
        var records = CsvRecordReader.Read(new StringReader("1,12\" PIPE, INC,3\n")).ToList();

        records.Should().ContainSingle().Which.Should().Equal("1", "12\" PIPE", " INC", "3");
    }
}
