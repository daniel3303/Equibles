using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsRenderTopHoldersTableDateCalendarTests
{
    private static readonly MethodInfo RenderTopHoldersTableMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "RenderTopHoldersTable",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // RenderTopHoldersTable renders its "as of {targetDate:yyyy-MM-dd}" header
    // with no IFormatProvider, so the date formats through the thread
    // CurrentCulture's calendar. The digit-separator sibling test pins de-DE
    // (a Gregorian-calendar locale, so the date is unaffected); this attacks
    // the orthogonal calendar axis. Under th-TH (Buddhist, year + 543) the
    // header must still be the Gregorian ISO date — same bug class as the
    // DateOptionTagHelper calendar issue (GH-2654).
    [Fact]
    public void RenderTopHoldersTable_UnderNonGregorianCalendar_RendersGregorianIsoDate()
    {
        var stock = new CommonStock { Ticker = "AAPL", Name = "Apple Inc." };
        var holder = new InstitutionalHolder { Name = "ACME Capital" };
        var holdings = new List<InstitutionalHolding>
        {
            new()
            {
                InstitutionalHolder = holder,
                Shares = 1_234_567,
                Value = 1_234_567_890L,
            },
        };
        object[] args =
        [
            stock,
            "AAPL",
            new DateOnly(2024, 12, 31),
            3,
            9_876_543L,
            9_876_543_210L,
            holdings,
            1m,
        ];

        var original = CultureInfo.CurrentCulture;
        string output;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            output = (string)RenderTopHoldersTableMethod.Invoke(null, args);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        output
            .Should()
            .Contain(
                "as of 2024-12-31:",
                "the report date must be a Gregorian ISO-8601 date regardless of host calendar; under th-TH the bare :yyyy-MM-dd specifier renders the Buddhist year 2567 instead of 2024"
            );
    }
}
