using System.Globalization;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceGetFileNamesThaiCultureTests
{
    // Contract: file names follow SEC EDGAR's literal cnsfails{YYYYMM}{a|b}.zip
    // URL scheme, where YYYY is the *Gregorian* year. The body builds each name
    // via `current.ToString("yyyyMM")` with no IFormatProvider — under th-TH
    // (and other cultures whose default DateTimeFormatInfo.Calendar is non-
    // Gregorian), DateOnly.ToString honors the culture's calendar, so the year
    // formats in Buddhist (Gregorian + 543). 2017-06 then renders as "256006",
    // every URL 404s, and the scrape silently produces zero records on Thai-
    // locale hosts. Parallel precedent: FredImportServiceParseDateHijriCulture.
    [Fact(Skip = "GH-1679 — GetFileNames emits Buddhist-calendar year under th-TH")]
    public void GetFileNames_UnderThaiCulture_EmitsGregorianYearInFileName()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            var fileNames = FtdImportService.GetFileNames(new DateOnly(2017, 6, 1));

            fileNames.Should().Contain("cnsfails201706b.zip");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
