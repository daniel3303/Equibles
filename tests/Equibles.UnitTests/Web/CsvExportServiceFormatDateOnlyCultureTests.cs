using System.Globalization;
using Equibles.Web.Services;

namespace Equibles.UnitTests.Web;

public class CsvExportServiceFormatDateOnlyCultureTests
{
    [Fact(
        Skip = "GH-1186 — Format(DateOnly) doesn't pass InvariantCulture so the thread culture's calendar leaks through (th-TH shifts year by +543)."
    )]
    public void Format_DateOnly_ThaiBuddhistCulture_StillEmitsGregorianIsoDate()
    {
        // Contract (CsvExportService XML doc): "Numeric / DateOnly conversions use the
        // invariant culture so the output is stable across hosts regardless of the
        // request thread's culture." A th-TH host defaults to ThaiBuddhistCalendar,
        // which would otherwise shift the year by +543 (e.g. 2024 -> 2567).
        var prev = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("th-TH");
            CsvExportService.Format(new DateOnly(2024, 6, 30)).Should().Be("2024-06-30");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
