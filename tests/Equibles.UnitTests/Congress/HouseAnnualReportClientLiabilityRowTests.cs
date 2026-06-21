using Equibles.Congress.Data.Models;
using static Equibles.Congress.HostedService.Services.HouseAnnualReportClient;

namespace Equibles.UnitTests.Congress;

public class HouseAnnualReportClientLiabilityRowTests
{
    private static List<ScheduleToken> Tokens(params (string text, double left)[] words) =>
        words.Select(w => new ScheduleToken(w.text, w.left)).ToList();

    // Contract: a Schedule D row carries Type, Creditor, and an Amount-of-
    // Liability range in column-aligned cells; it materializes as a Liability
    // line described "Type (Creditor)" with that range. (Schedule A must be
    // present for the report to count as electronic, so a bare "S A:" header
    // precedes the liability schedule.)
    [Fact]
    public void ParseScheduleLines_ScheduleDLiabilityRow_ParsesTypeCreditorAndRange()
    {
        var result = ParseScheduleLines([
            Tokens(("S", 22), ("A:", 92)),
            Tokens(("S", 22), ("D:", 92)),
            // Schedule D header: Owner | Creditor | Date Incurred | Type | Amount of Liability
            Tokens(
                ("Owner", 25),
                ("Creditor", 100),
                ("Date", 200),
                ("Incurred", 230),
                ("Type", 300),
                ("Amount", 360),
                ("of", 390),
                ("Liability", 405)
            ),
            Tokens(
                ("Self", 25),
                ("Bank", 100),
                ("of", 125),
                ("America", 150),
                ("2015", 200),
                ("Mortgage", 300),
                ("$50,001", 360),
                ("-", 390),
                ("$100,000", 400)
            ),
        ]);

        var line = result.Should().ContainSingle().Subject;
        line.Kind.Should().Be(CongressionalDisclosureLineKind.Liability);
        line.Description.Should().Be("Mortgage (Bank of America)");
        line.RangeMinimum.Should().Be(50_001);
        line.RangeMaximum.Should().Be(100_000);
    }
}
