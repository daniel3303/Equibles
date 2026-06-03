using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

public class FormAdvCsvParserUnkeyableRowTests
{
    [Fact]
    public void Parse_RowsWithBlankOrNonNumericCrd_AreSkippedWhileKeyableRowsSurvive()
    {
        // Contract: "Rows without a usable Organization CRD number are skipped —
        // without it an adviser cannot be keyed." A blank or non-numeric CRD in the
        // untrusted SEC export must drop only that row, never abort the import or
        // emit an unkeyable adviser.
        var csv =
            "Organization CRD#,Legal Name\n"
            + "231,Valid Adviser\n"
            + ",Blank Crd Adviser\n"
            + "N/A,Non Numeric Crd Adviser\n";

        using var reader = new StringReader(csv);
        var advisers = FormAdvCsvParser.Parse(reader).ToList();

        advisers.Should().ContainSingle();
        advisers[0].Crd.Should().Be(231);
        advisers[0].LegalName.Should().Be("Valid Adviser");
    }
}
