using Equibles.Integrations.Sec.FormAdv;

namespace Equibles.UnitTests.Sec;

public class FormAdvCsvParserDuplicateHeaderTests
{
    [Fact]
    public void Parse_DuplicateColumnHeader_BindsFieldToFirstOccurrence()
    {
        // Contract (BuildColumnMap doc): "Keep the first occurrence." The SEC export is
        // untrusted and 260+ columns wide; if a header name repeats, a field must bind to the
        // FIRST matching column, not the last. No existing test exercises the duplicate path.
        var csv = "Organization CRD#,Legal Name,Legal Name\n" + "1001,First Wins,Second Ignored\n";

        using var reader = new StringReader(csv);
        var advisers = FormAdvCsvParser.Parse(reader).ToList();

        advisers.Should().ContainSingle();
        advisers[0].LegalName.Should().Be("First Wins");
    }
}
