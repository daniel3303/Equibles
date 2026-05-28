using System.Globalization;
using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryParseMasterIndexLineHijriCultureTests
{
    private static readonly MethodInfo TryParseMethod = typeof(SecEdgarClient).GetMethod(
        "TryParseMasterIndexLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // The DateFiled column of an EDGAR master.idx row is ISO yyyy-MM-dd,
    // and `fallbackDate` exists only for genuinely-malformed rows (pinned by
    // the sibling malformed-date test). A valid ISO filing date must
    // therefore round-trip to itself on any host — every other date helper
    // in this code path (InsiderTradingFilingProcessor.TryParseTransactionDate,
    // FredImportService.ParseDate GH-1501) was already hardened with
    // InvariantCulture for exactly this reason. TryParseMasterIndexLine still
    // calls `DateOnly.TryParse(dateFiled, out var d)` with no culture, so under
    // ar-SA (Umm al-Qura) the ISO date fails to parse and DateFiled silently
    // becomes the fallback — every 13F-HR filing in the daily-index sweep is
    // stamped with the wrong filing date on a Hijri-locale host. Pin: a valid
    // ISO DateFiled round-trips regardless of thread culture (fallback is set
    // distinct so a fallback substitution is visible).
    [Fact(
        Skip = "GH-2649 — TryParseMasterIndexLine omits InvariantCulture; ISO DateFiled misparses under ar-SA"
    )]
    public void TryParseMasterIndexLine_IsoDateFiledUnderHijriCulture_UsesParsedDateNotFallback()
    {
        const string line =
            "1067983|BERKSHIRE HATHAWAY INC|13F-HR|2024-11-14|edgar/data/1067983/0000950123-24-009876.txt";
        var fallbackDate = new DateOnly(1999, 12, 31);
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            object[] args = [line, fallbackDate, null];
            var success = (bool)TryParseMethod.Invoke(null, args);
            var entry = (EdgarDailyIndexEntry)args[2];

            success.Should().BeTrue();
            entry.DateFiled.Should().Be(new DateOnly(2024, 11, 14));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
