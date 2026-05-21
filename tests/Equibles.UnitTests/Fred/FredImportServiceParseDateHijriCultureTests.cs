using System.Globalization;
using System.Reflection;
using Equibles.Fred.HostedService.Services;

namespace Equibles.UnitTests.Fred;

public class FredImportServiceParseDateHijriCultureTests
{
    // ParseDate is the helper that hydrates FredSeries.ObservationStart and
    // FredSeries.ObservationEnd from the FRED series-metadata payload. Per the
    // strict-InvariantCulture precedent established for the sibling
    // ParseObservationDates by GH-1501, an ISO `yyyy-MM-dd` date must round-trip
    // to the expected DateOnly regardless of thread culture — every FRED date
    // is emitted as ISO. The helper's signature has the right intent (returns
    // DateOnly?) but the implementation calls `DateOnly.TryParse(value, out)`
    // with no explicit InvariantCulture, so under ar-SA (Umm al-Qura) the parse
    // fails and the helper returns null. ObservationStart / ObservationEnd are
    // then persisted as null, silently corrupting every freshly-seeded
    // FredSeries on Hijri-locale hosts.
    [Fact]
    public void ParseDate_IsoDateUnderHijriCulture_DoesNotReturnNull()
    {
        var method = typeof(FredImportService).GetMethod(
            "ParseDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var result = (DateOnly?)method.Invoke(null, ["2024-01-15"]);

            result.Should().Be(new DateOnly(2024, 1, 15));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
