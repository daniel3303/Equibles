using System.Globalization;
using System.Reflection;
using Equibles.CommonStocks.Data.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

public class DocumentTextToolsFormatDocumentHeaderCalendarTests
{
    // FormatDocumentHeader renders "filed {ReportingDate:yyyy-MM-dd}" with no
    // IFormatProvider, so the year formats through the thread CurrentCulture's
    // calendar. The sibling header test pins the ISO shape under the default
    // (Gregorian) culture; this attacks the orthogonal calendar axis. Under
    // th-TH (Buddhist, year + 543) the banner the LLM consumer parses must
    // still carry the Gregorian ISO date — same bug class fixed for the
    // holdings MCP date headers (GH-2681).
    [Fact(Skip = "GH-2773 — bare :yyyy-MM-dd renders Buddhist year under th-TH calendar")]
    public void FormatDocumentHeader_UnderNonGregorianCalendar_RendersGregorianIsoDate()
    {
        var document = new Document
        {
            CommonStock = new CommonStock { Name = "Apple Inc.", Ticker = "AAPL" },
            DocumentType = DocumentType.TenK,
            ReportingDate = new DateOnly(2026, 5, 27),
        };

        var method = typeof(DocumentTextTools).GetMethod(
            "FormatDocumentHeader",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            result = (string)method!.Invoke(null, [document]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        result
            .Should()
            .Be(
                "Apple Inc. (AAPL) 10-K filed 2026-05-27",
                "the filing date in the MCP banner must be a Gregorian ISO-8601 date regardless of host calendar; under th-TH the bare :yyyy-MM-dd specifier renders the Buddhist year 2569"
            );
    }
}
