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

    [Fact]
    public void ParseDate_ModernYyyyMmDdFormat_Parses() {
        // Sibling pin to the legacy-format test above. The two tests together pin BOTH
        // date-format branches of ParseDate. The risk this catches is asymmetric and
        // unreachable from the legacy sibling alone: a regression that deletes (or
        // breaks) the FIRST TryParseExact call — for the modern "yyyy-MM-dd" format —
        // would slip past the existing tests:
        //   - "250115" still matches the surviving yyMMdd branch → legacy test passes
        //   - "not-a-date" still returns null → unparseable test passes
        // …but every modern Report_Date_as_YYYY-MM-DD row would silently parse to null
        // and get dropped by `if (date == null) continue;` in ImportYear. CFTC's recent
        // history files (post-2010 or so) use the modern format exclusively, so an
        // "always-legacy-only" regression would silently stop importing every recent
        // year while the legacy fallback keeps the pre-2010 path working — exactly the
        // sort of partial failure that looks healthy until someone audits coverage.
        //
        // Pin the modern format with a representative date that ONLY the yyyy-MM-dd
        // branch can match (10 chars, dashes — yyMMdd's TryParseExact rejects it on
        // length). The pair (legacy → date, modern → date) distinguishes a working
        // two-branch parser from a refactor that collapses to a single branch.
        var result = (DateOnly?)ParseDateMethod.Invoke(null, ["2025-01-15"]);

        result.Should().Be(new DateOnly(2025, 1, 15));
    }

    [Fact]
    public void ParseDate_UnparseableValue_ReturnsNull() {
        // ImportYear's foreach skips malformed rows via `if (date == null) continue;`,
        // so returning null on bad input — rather than throwing — is the contract that
        // keeps the importer from crashing an entire year on a single bad row. A
        // refactor that swapped TryParseExact for DateOnly.Parse would throw
        // FormatException and break that contract silently.
        var result = (DateOnly?)ParseDateMethod.Invoke(null, ["not-a-date"]);

        result.Should().BeNull();
    }
}
