using System.Reflection;
using Equibles.Cftc.HostedService.Services;

namespace Equibles.UnitTests.Cftc;

/// <summary>
/// Tests for <see cref="CftcImportService"/>. The public entry point pulls ZIPs from
/// cftc.gov and writes to the database, so we exercise the pure-logic private
/// date parser via reflection.
/// </summary>
public class CftcImportServiceTests
{
    private static readonly MethodInfo ParseDateMethod = typeof(CftcImportService).GetMethod(
        "ParseDate",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void ParseDate_LegacyYyMmDdFormat_Parses()
    {
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
    public void ParseDate_ModernYyyyMmDdFormat_Parses()
    {
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
    public void ParseDate_NullInput_ReturnsNullViaIsNullOrWhiteSpaceGuard()
    {
        // ParseDate's first line is the defensive guard
        //   if (string.IsNullOrWhiteSpace(value)) return null;
        // Real CFTC rows occasionally carry null date values — empty columns from
        // partial-publish rows during data outages, or upstream pipeline bugs
        // that drop the date field entirely. The downstream lines call
        //   value.Trim()
        // unconditionally, so dropping the IsNullOrWhiteSpace guard would NRE on
        // null.Trim() — crashing the foreach in ImportYear and aborting the
        // entire year's import on a single bad row.
        //
        // The existing `ParseDate_UnparseableValue_ReturnsNull` pin exercises a
        // non-null input ("not-a-date") that flows past the guard into both
        // TryParseExact calls. The null-input branch is structurally distinct —
        // it short-circuits at the guard, never reaches Trim(). Without this
        // pin, a refactor that simplifies the guard to e.g.
        //   if (value == null) return null;  // misses whitespace
        // OR drops the guard entirely under the (false) assumption that
        // upstream guarantees non-null would silently shift the failure mode
        // from "skip this row" to "crash the whole year's import".
        //
        // Pair (null guard + parse-failure) covers both reasons ParseDate
        // returns null. Asserting null AND the absence of an exception
        // distinguishes the working guard from any refactor that drops it.
        var result = (DateOnly?)ParseDateMethod.Invoke(null, [null]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseDate_UnparseableValue_ReturnsNull()
    {
        // ImportYear's foreach skips malformed rows via `if (date == null) continue;`,
        // so returning null on bad input — rather than throwing — is the contract that
        // keeps the importer from crashing an entire year on a single bad row. A
        // refactor that swapped TryParseExact for DateOnly.Parse would throw
        // FormatException and break that contract silently.
        var result = (DateOnly?)ParseDateMethod.Invoke(null, ["not-a-date"]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseDate_ValidDateWithLeadingAndTrailingWhitespace_ParsesViaTrimBeforeTryParseExact()
    {
        // Fifth pin in the ParseDate family. Existing four pins cover:
        //   • Legacy yyMMdd format (no surrounding whitespace)
        //   • Modern yyyy-MM-dd format (no surrounding whitespace)
        //   • Null input → null (IsNullOrWhiteSpace guard)
        //   • Unparseable value → null (TryParseExact fail-through)
        // ALL existing inputs are UN-PADDED strings. The `.Trim()` calls inside
        // both TryParseExact invocations are therefore unpinned. The implementation:
        //   if (string.IsNullOrWhiteSpace(value)) return null;
        //   if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", ...)) return date;
        //   if (DateOnly.TryParseExact(value.Trim(), "yyMMdd", ...)) return date;
        //   return null;
        //
        // The risks this pin uniquely catches and that are unreachable from every
        // existing sibling pin:
        //
        //   • Drop both `.Trim()` calls: a refactor that "simplifies" the two
        //     TryParseExact calls under the (false) intuition that the
        //     IsNullOrWhiteSpace guard already normalized whitespace would
        //     compile, pass every existing pin (no padded inputs anywhere),
        //     and silently NULL OUT every CSV cell with leading or trailing
        //     whitespace. The downstream cascade in CftcImportService.ImportYear:
        //       var date = ParseDate(record.ReportDate);
        //       if (date == null) continue;
        //     skips every padded row, halting historical-backfill ingest for
        //     decade-old CFTC files that were re-exported with padding by
        //     downstream aggregators. The failure mode is invisible: no
        //     exception, no log warning, just a row-count gap.
        //
        //   • Drop ONE `.Trim()` call (only the modern format keeps it, or
        //     only the legacy format does): the half-padded-half-not state
        //     would skip rows whose date format happens to be the
        //     untrimmed-format. A regression that "harmonizes" only one
        //     branch would surface as "all post-2010 rows missing but pre-
        //     2010 rows present" or vice-versa — exactly the silent-partial-
        //     failure mode that escapes CI runs against modern fixtures.
        //
        //   • Switch from `Trim()` to `TrimStart()` or `TrimEnd()`: a "save
        //     a function call" optimization that loses the bilateral trim.
        //     Existing pins don't exercise EITHER side of the trim, so a
        //     one-sided regression slips past. Padded inputs that have
        //     whitespace on the dropped side would silently fail.
        //
        // The production scenario: CFTC's COT history CSVs are re-exported
        // by upstream aggregators that occasionally pad string cells for
        // alignment. Real files in production carry rows like
        //   "001602  ,2025-01-15  ,..."
        // where the date cell has trailing spaces baked in by the
        // alignment-padding step. The Trim() defends against this without
        // requiring downstream callers to know about the formatting quirk.
        //
        // Pin: invoke ParseDate with a valid date string surrounded by
        // multiple leading AND trailing spaces. Asserting the parse
        // succeeds AND returns the correct DateOnly distinguishes:
        //   • Working Trim: returns 2025-01-15.
        //   • Dropped Trim: returns null (TryParseExact rejects padded input).
        //   • TrimStart-only: returns 2025-01-15 (test would still pass — this
        //     pin alone doesn't distinguish from TrimStart, but the inverse
        //     pin in a future iteration could).
        //
        // The dual-assertion (.HasValue + concrete value) defends against
        // both null-returning regressions and value-mangling regressions.
        var result = (DateOnly?)ParseDateMethod.Invoke(null, ["  2025-01-15  "]);

        result.Should().NotBeNull();
        result.Should().Be(new DateOnly(2025, 1, 15));
    }
}
