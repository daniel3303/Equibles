using System.Reflection;
using Equibles.Cftc.HostedService.Services;

namespace Equibles.UnitTests.Cftc;

/// <summary>
/// Tests for <see cref="CftcImportService"/>. The public entry point pulls ZIPs from
/// cftc.gov and writes to the database, so we exercise the pure-logic private
/// date parser via reflection.
/// </summary>
public class CftcImportServiceTests {
    private static readonly MethodInfo ParseDateMethod = typeof(CftcImportService)
        .GetMethod("ParseDate", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void ParseDate_LegacyYyMmDdFormat_Parses() {
        // CFTC history files mix two date encodings: modern "Report_Date_as_YYYY-MM-DD"
        // and legacy "As_of_Date_In_Form_YYMMDD". ParseDate must fall back to the
        // legacy yyMMdd format after the yyyy-MM-dd attempt — without the fallback,
        // every historical row that only carries the legacy date silently parses as
        // null and gets dropped by the importer. Pin the legacy path so a refactor
        // that removes it doesn't lose decades of COT history.
        var result = (DateOnly?)ParseDateMethod.Invoke(null, ["250115"]);

        result.Should().Be(new DateOnly(2025, 1, 15));
    }
}
