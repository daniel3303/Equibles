using System.Globalization;
using System.Reflection;
using Equibles.Fred.HostedService.Services;
using Equibles.Integrations.Fred.Models;

namespace Equibles.UnitTests.Fred;

public class FredImportServiceParseObservationDatesHijriCultureTests
{
    [Fact]
    public void ParseObservationDates_IsoDateUnderHijriCulture_DoesNotSilentlyDropRecord()
    {
        // FRED's observation feed always emits ISO `yyyy-MM-dd` dates. The
        // existing pin (ParseObservationDatesNullDateTests) covers the
        // null-date silent-drop arm; this pin attacks a separate risk
        // surface: the helper's inner `DateOnly.TryParse(r.Date, out var
        // date)` carries NO explicit `CultureInfo.InvariantCulture`
        // argument, so the parse uses the thread's CurrentCulture.
        //
        // Under ar-SA Umm al-Qura, the default `DateOnly.TryParse` overload
        // uses the Hijri calendar — the precise failure mode the sister
        // SEC code path (`InsiderTradingFilingProcessor.ParseTransaction`)
        // explicitly hardens against by passing `InvariantCulture`. If
        // FRED's helper exhibits the same fragility, a Worker host whose
        // locale defaults to ar-SA would silently drop EVERY FRED
        // observation as "unparseable date" — the per-record loop simply
        // skips them.
        //
        // Pin the contract a caller would reasonably rely on: a record
        // with a well-formed ISO date must produce one entry in the
        // output regardless of thread culture. Wrap in try/finally so
        // the test restores the original culture even if the assertion
        // fires.
        var method = typeof(FredImportService).GetMethod(
            "ParseObservationDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var records = new List<FredObservationRecord>
            {
                new() { Date = "2024-01-15", Value = "5.33" },
            };

            var result = (System.Collections.IList)method.Invoke(null, [records]);

            result.Count.Should().Be(1);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
