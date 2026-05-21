using System.Globalization;
using System.Reflection;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorTryParseTransactionDateHijriCultureTests
{
    // Sibling to GH-1501's pin on FredImportService.ParseObservationDates,
    // which lacked the explicit-InvariantCulture hardening and silently dropped
    // every ISO-formatted observation under ar-SA Umm al-Qura. This file's
    // target — TryParseTransactionDate, extracted in #1507 — has the
    // hardening (the WHY-comment documents it: "Parse it culture-independently
    // — under a non-Gregorian host culture (e.g. ar-SA Umm al-Qura)
    // culture-sensitive TryParse fails and every insider transaction would be
    // silently dropped").
    //
    // Pin the documented contract: an ISO Form 4 `transactionDate` parses
    // correctly under ar-SA. The test passes today because the helper passes
    // `CultureInfo.InvariantCulture` explicitly; a "simplification" refactor
    // dropping that argument (or switching to the no-culture
    // `DateOnly.TryParse(string)` overload) would compile cleanly, pass every
    // happy-path test (those all run under en-US), and silently drop every
    // Form 4 transaction on any worker host whose locale defaults to ar-SA.
    [Fact]
    public void TryParseTransactionDate_IsoDateUnderHijriCulture_StillParsesViaInvariantCulture()
    {
        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "TryParseTransactionDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var args = new object[] { "2024-03-15", default(DateOnly) };
            var parsed = (bool)method.Invoke(null, args);

            parsed.Should().BeTrue();
            ((DateOnly)args[1]).Should().Be(new DateOnly(2024, 3, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
