using System.Reflection;
using Equibles.Fred.HostedService.Services;
using Equibles.Integrations.Fred.Models;

namespace Equibles.UnitTests.Fred;

public class FredImportServiceParseObservationDatesNullDateTests
{
    private static readonly MethodInfo ParseObservationDatesMethod =
        typeof(FredImportService).GetMethod(
            "ParseObservationDates",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    // ParseObservationDates' contract (extracted in #1488): records with
    // unparseable Date strings are silently dropped. The implementation calls
    // `DateOnly.TryParse(r.Date, out var date)` — and TryParse returns false
    // (without throwing) when the input is null. A "defensive" refactor adding
    // an explicit `r.Date != null` short-circuit would behave the same, but a
    // regression that switched to `DateOnly.Parse` (or any throw-on-null
    // shape) would crash ImportSeries on every FRED response that contains a
    // sparse row. Pin the silent-drop contract with a record whose Date is
    // null, mixed with a valid record to confirm the loop continues past it.
    [Fact]
    public void ParseObservationDates_RecordWithNullDate_IsSilentlyDroppedNotThrown()
    {
        var records = new List<FredObservationRecord>
        {
            new() { Date = null, Value = "5.33" },
            new() { Date = "2024-01-15", Value = "5.40" },
        };

        var result = (System.Collections.IList)ParseObservationDatesMethod.Invoke(null, [records]);

        result.Count.Should().Be(1);
    }
}
