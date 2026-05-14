using System.Reflection;
using Equibles.Integrations.Cftc;
using Equibles.Integrations.Cftc.Models;

namespace Equibles.UnitTests.Integrations;

/// <summary>
/// Tests for <see cref="CftcClient"/>. The public entry point downloads ZIPs from
/// cftc.gov, so we exercise the pure-logic private CSV splitter via reflection.
/// </summary>
public class CftcClientTests
{
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient).GetMethod(
        "SplitCsvLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static readonly MethodInfo ParseLongMethod = typeof(CftcClient).GetMethod(
        "ParseLong",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static readonly MethodInfo BuildColumnIndexMethod = typeof(CftcClient).GetMethod(
        "BuildColumnIndex",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static readonly MethodInfo ParseLineMethod = typeof(CftcClient).GetMethod(
        "ParseLine",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    private static readonly MethodInfo GetFieldMethod = typeof(CftcClient).GetMethod(
        "GetField",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void GetField_ColumnIndexExceedsRowLength_ReturnsNullInsteadOfIndexOutOfRange()
    {
        // GetField's bounds guard is `!columnIndex.TryGetValue(...) || idx >= fields.Length`.
        // The TryGetValue branch fires when the column name is absent from the header.
        // The `idx >= fields.Length` branch fires when the COLUMN exists in the header
        // index but the row is SHORTER than the header — a real CFTC data shape that
        // happens when:
        //   • CFTC truncates rows during partial-publish windows mid-week.
        //   • A row's trailing fields get stripped by upstream CSV cleaners that drop
        //     empty trailing columns.
        //   • An aggregator re-emits historical data with only the populated columns.
        //
        // Without the guard, the next line `fields[idx].Trim()` would throw
        // IndexOutOfRangeException — crashing the entire ParseLine call, which propagates
        // through ParseZipArchive's foreach and aborts the whole year's import on a
        // single bad row. The catch in CftcImportService.ImportYear logs the error but
        // by that point the year's records list is already partially built; rolling
        // forward typically loses unrelated rows in the same batch.
        //
        // The existing pins (BuildColumnIndex, ParseLong, SplitCsvLine, ParseLine
        // legacy-date) don't exercise GetField with a mismatched-length row. Pin the
        // guard with a 2-field row and a column index that points at position 5
        // (which would be valid for a normal CFTC row but is past the end of this
        // truncated one).
        //
        // Construction: header with 6 columns (so the index has entries at positions
        // 0..5), data row with only 2 fields. Asking for the "Pct_of_OI_Comm_Short_All"
        // at index 5 must return null safely.
        var headerLine =
            "Market_and_Exchange_Names,CFTC_Contract_Market_Code,Open_Interest_All,NonComm_Positions_Long_All,Comm_Positions_Long_All,Pct_of_OI_Comm_Short_All";
        var columnIndex =
            (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);
        var truncatedFields = new[] { "WHEAT-SRW - CHICAGO BOARD OF TRADE", "001602" };

        var result = (string)
            GetFieldMethod.Invoke(null, [truncatedFields, columnIndex, "Pct_of_OI_Comm_Short_All"]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseLine_LegacyDateColumnOnly_FallsBackToAsOfDateInFormYyMmDd()
    {
        // CFTC's COT history files mix two CSV schemas across the decades-long backfill:
        //   • Modern (post-2010ish): "Report_Date_as_YYYY-MM-DD" column with ISO dates
        //     like "2025-01-15".
        //   • Legacy: "As_of_Date_In_Form_YYMMDD" column with packed dates like "250115".
        // ParseLine reads ReportDate with a `?? GetField(..., legacy)` fallback so a CSV
        // that lacks the modern column still produces a ReportDate value. Without the
        // fallback (e.g. a refactor that "simplifies" the `??` chain to just the modern
        // column), every historical file would yield records with null ReportDate;
        // CftcImportService.ImportYear's `if (date == null) continue;` would then drop
        // every row, silently zeroing out the pre-2010 backfill while modern years keep
        // working — exactly the partial-failure mode that escapes integration tests
        // built only against recent fixtures.
        //
        // Sibling pins in CftcImportServiceTests cover the two date FORMATS at the
        // ParseDate level (yyyy-MM-dd and yyMMdd). The COLUMN-NAME fallback at the
        // CftcClient level is structurally distinct and previously unpinned. The pair
        // (format fallback at ParseDate + column-name fallback at ParseLine) covers
        // both reasons a date might be missing.
        //
        // Pin: a single-row CSV whose header carries ONLY the legacy column. The
        // returned record's ReportDate must equal the legacy raw string "250115" —
        // ParseLine reads the value verbatim and leaves date-string format
        // interpretation to the downstream ParseDate helper.
        var headerLine = "CFTC_Contract_Market_Code,As_of_Date_In_Form_YYMMDD";
        var columnIndex =
            (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);
        var line = "001602,250115";

        var record = (CftcReportRecord)ParseLineMethod.Invoke(null, [line, columnIndex]);

        record.Should().NotBeNull();
        record.ReportDate.Should().Be("250115");
        record.ContractMarketCode.Should().Be("001602");
    }

    [Fact]
    public void ParseLine_BothModernAndLegacyDateColumnsPresent_PrefersModernViaCoalesceOperandOrder()
    {
        // Sibling to ParseLine_LegacyDateColumnOnly_FallsBackToAsOfDateInFormYyMmDd. That
        // pin asserts the FALLBACK behavior — when the modern "Report_Date_as_YYYY-MM-DD"
        // column is absent, ParseLine reads from the legacy "As_of_Date_In_Form_YYMMDD"
        // column instead. This pin covers the STRUCTURALLY DISTINCT case where both
        // columns are present in the same CSV — and the `??` coalesce in ParseLine must
        // prefer the modern column:
        //   ReportDate = GetField(fields, columnIndex, "Report_Date_as_YYYY-MM-DD")
        //                ?? GetField(fields, columnIndex, "As_of_Date_In_Form_YYMMDD"),
        //
        // Why this case is real production: CFTC's COT history files for the current
        // decade emit BOTH date columns — the modern ISO column is the canonical one,
        // and the legacy YYMMDD column is shipped alongside for back-compat with older
        // tooling. The CftcImportService.ParseDate helper handles both formats, so
        // either column's value can be parsed downstream. The choice of which column
        // FEEDS the parser is therefore a silent decision — picking the wrong one
        // produces NO error, just a different (correct-format-but-different-encoded)
        // date string. The downstream parser succeeds on both, so the failure mode
        // is invisible at every CI layer except a pin like this one.
        //
        // The risk this pin catches is asymmetric and UNREACHABLE from the legacy-only
        // sibling pin:
        //   • A refactor that swaps the `??` operand order to `legacy ?? modern` would
        //     compile cleanly. The legacy-only sibling test still passes: that test's
        //     header has ONLY the legacy column, so `GetField(modern)` returns null,
        //     and `legacy ?? modern` evaluates to the legacy value just like
        //     `modern ?? legacy` would. The two operand orders are observationally
        //     indistinguishable when modern is absent. They diverge ONLY when both
        //     are present — exactly the case this pin tests.
        //   • A refactor that drops the `??` entirely and reads only ONE column —
        //     either "swap to a single canonical column" or a "we don't need the
        //     fallback anymore, the modern column is universal" simplification —
        //     would also flip behavior. If only-modern survives: the legacy-only
        //     sibling fails (good, catches it). If only-legacy survives: BOTH this
        //     pin AND the legacy-only sibling pass (legacy-only test still finds
        //     legacy column), but every record from a modern file silently uses
        //     the legacy-format date string. Downstream consumers that didn't read
        //     it would have to detect the format change.
        //
        // Production analogue: CftcImportService.ImportYear feeds the raw ReportDate
        // string into ParseDate, which tries yyyy-MM-dd FIRST then falls back to
        // yyMMdd. Both encodings produce a correct DateOnly. So an operand-swap
        // regression would NOT corrupt the data — but it WOULD switch the wire
        // format from "2025-01-15" to "250115" for every record. Downstream
        // logging, error reporting, and anything that compares raw ReportDate
        // strings (debugging aids, support tickets) would see the legacy
        // encoding mixed into a dataset that's supposed to use the modern one.
        //
        // The complementary asymmetry to the legacy-only pin: that one asserts
        // GetField returns the legacy value when the modern column is absent.
        // This pin asserts that when both are present, the MODERN value wins.
        // The pair distinguishes the THREE possible coalesce regressions:
        //   1. Operand swap (legacy ?? modern): caught only here (legacy-only
        //      still passes because legacy column is present).
        //   2. Drop modern entirely (legacy only): caught here AND legacy-only
        //      still works (legacy column present); diagnosis depends on which
        //      test failed.
        //   3. Drop legacy entirely (modern only): caught by legacy-only sibling
        //      (returns null instead of legacy value); this pin still passes
        //      (modern column present).
        //
        // Construction: header with BOTH date columns. Modern column at index 1
        // holds "2025-01-15"; legacy column at index 2 holds "250115" (the same
        // date in legacy encoding, so the test only catches operand-swap
        // regressions if the assertion compares the wire-format string, not
        // the parsed DateOnly). The data row provides BOTH date values
        // populated — neither is empty/blank — so the GetField empty-cell
        // arm (separately pinned in GetField_CellEmptyAfterTrim) doesn't fire
        // for either column. Assertion compares against the modern wire-format
        // string verbatim, which can ONLY hold for `modern ?? legacy` order.
        var headerLine =
            "CFTC_Contract_Market_Code,Report_Date_as_YYYY-MM-DD,As_of_Date_In_Form_YYMMDD";
        var columnIndex =
            (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);
        var line = "001602,2025-01-15,250115";

        var record = (CftcReportRecord)ParseLineMethod.Invoke(null, [line, columnIndex]);

        record.Should().NotBeNull();
        record.ReportDate.Should().Be("2025-01-15");
    }

    [Fact]
    public void BuildColumnIndex_QuotedHeaderName_IndexedWithoutQuotes()
    {
        // CFTC's COT history CSVs ship header rows in two flavours that vary by year:
        // bare (`Open_Interest_All`) and double-quoted (`"Open_Interest_All"`). The
        // downstream ParseLine looks up each column by its bare-string name through
        // GetField, so the index built here must strip surrounding quotes — drop the
        // `.Trim('"')` and every quoted-header file's GetField calls miss, falling
        // through to `idx >= fields.Length` returning null, and every numeric column
        // silently parses to zero. Pin the quote-strip path on a single quoted header
        // and a follow-on case-insensitive lookup (the index uses OrdinalIgnoreCase),
        // so a regression that swaps the comparer or removes the trim is caught.
        var headerLine = "\"Open_Interest_All\",Some_Other";

        var index = (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);

        index.Should().ContainKey("Open_Interest_All").WhoseValue.Should().Be(0);
        // OrdinalIgnoreCase: bare lookup with different casing must still hit.
        index.Should().ContainKey("open_interest_all");
    }

    [Fact]
    public void ParseLong_ValueWithThousandSeparatorCommas_StripsAndParses()
    {
        // CFTC's legacy COT history files format every numeric position column
        // (Open_Interest_All, NonComm_Positions_*, Comm_Positions_*, etc.) with
        // thousand-separator commas: "1,234,567". ParseLong strips those commas
        // before calling long.TryParse with InvariantCulture — drop the
        // .Replace(",", "") and every multi-comma value falls back to null/0,
        // silently writing zeros for the bulk of CFTC's position dataset. Pin
        // the strip path on a multi-group thousand-separated value so a
        // regression that simplifies the parse path is caught at test time.
        var result = (long?)ParseLongMethod.Invoke(null, ["1,234,567"]);

        result.Should().Be(1_234_567L);
    }

    [Fact]
    public void ParseInt_ValueWithThousandSeparatorCommas_StripsAndParses()
    {
        // Sibling to `ParseLong_ValueWithThousandSeparatorCommas_StripsAndParses`.
        // ParseInt and ParseLong share the same defensive idiom —
        //   `value.Replace(",", "")` BEFORE int/long.TryParse —
        // but they live in two distinct methods at lines 176 (ParseLong)
        // and 186 (ParseInt) and are wired to different schema fields.
        // ParseInt is exclusively used for the Traders_* columns
        // (Traders_Tot_All, Traders_NonComm_Long_All, Traders_NonComm_Short_All,
        // Traders_Comm_Long_All, Traders_Comm_Short_All): all small-cardinality
        // integers counting how many distinct reporting traders are on each
        // side of the market for a given contract.
        //
        // The risk this catches is asymmetric from the existing ParseLong
        // pin: a refactor that "tidies up" ParseInt by dropping the
        // .Replace(",", "") (under the assumption that CultureInfo
        // .InvariantCulture's int parser handles thousands separators)
        // compiles, passes the ParseLong pin (different method, untouched),
        // and silently nulls out the Traders_* columns whenever the
        // upstream CFTC file emits trader counts with comma separators.
        // CFTC's legacy historical files DO emit comma-separated values
        // for any column ≥1000 — the trader-count columns routinely cross
        // that threshold for the most-traded contracts (S&P 500 futures,
        // Treasury complex, oil), so this is not a hypothetical edge case.
        //
        // CultureInfo.InvariantCulture's default NumberStyles.Integer for
        // int.TryParse does NOT accept thousands separators (matching the
        // long behavior documented in the ParseLong pin) — that requires
        // NumberStyles.AllowThousands which TryParse's three-arg overload
        // doesn't set. Without the Replace, every comma-formatted trader
        // count becomes null, and the public CFTC positioning page silently
        // displays "0 reporting traders" for the highest-volume contracts.
        //
        // The pair (ParseLong + ParseInt pins) covers both numeric helpers
        // with the same defensive contract. The complementary asymmetry
        // (ParseDecimal does NOT do the replace) is intentional —
        // percentages are always sub-100 and never carry thousands
        // separators in CFTC's wire format. That asymmetry is captured
        // by NOT pinning ParseDecimal here.
        var parseInt = typeof(CftcClient).GetMethod(
            "ParseInt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (int?)parseInt!.Invoke(null, ["12,345"]);

        result.Should().Be(12_345);
    }

    [Fact]
    public void GetField_CellEmptyAfterTrim_ReturnsNullSoReportDateFallbackEngages()
    {
        // Sibling pin to GetField_ColumnIndexExceedsRowLength_ReturnsNullInsteadOfIndexOutOfRange.
        // GetField has THREE distinct return-null paths:
        //   1. Column name not in index           → `!columnIndex.TryGetValue(...)`
        //   2. Index points past end of row       → `idx >= fields.Length`     (existing pin)
        //   3. Cell value is empty/whitespace     → `string.IsNullOrEmpty(value.Trim())` (THIS pin)
        //
        // Arm #3 is the one that makes ReportDate's column-name fallback work. ParseLine
        // resolves ReportDate as:
        //   GetField(fields, columnIndex, "Report_Date_as_YYYY-MM-DD")
        //       ?? GetField(fields, columnIndex, "As_of_Date_In_Form_YYMMDD")
        // The `??` operator engages ONLY when the left side is `null`. If GetField
        // returned `""` (empty string) for a blank cell instead of `null`, the fallback
        // would never fire — the file would carry the modern column header but with empty
        // data, and every record would silently end up with ReportDate = "" instead of
        // the legacy date. CftcImportService.ParseDate then treats "" as unparseable
        // (via its `IsNullOrWhiteSpace` guard), returns null, and ImportYear's
        // `if (date == null) continue;` drops the row — wiping out the entire year.
        //
        // The risk this catches is asymmetric and unreachable from existing pins:
        //   • A refactor that "tidies up" GetField by removing the `IsNullOrEmpty`
        //     check (e.g. someone thinking the Trim alone is enough, since "empty
        //     string is falsy in many languages") compiles cleanly. Every existing
        //     GetField test passes — the bounds-check pin uses a column that's truly
        //     absent from the row (different arm), the BuildColumnIndex pin doesn't
        //     touch GetField at all, ParseLong/ParseInt operate on already-extracted
        //     non-null values, and the ParseLine legacy-date pin uses a header with
        //     ONLY the legacy column (so the modern GetField hits arm #1, not arm #3).
        //   • Result: a CFTC file with both modern AND legacy date columns, where the
        //     modern column has blank cells but the legacy column has values, would
        //     produce empty ReportDate strings instead of falling back to the legacy
        //     column. This shape exists in CFTC's historical re-publishes where the
        //     modern column was added retroactively but only populated for the most
        //     recent rows.
        //
        // Pin: a row with a whitespace-only cell at the index of a real column. The
        // returned value must be null (so `??` would fall through), NOT empty string,
        // NOT whitespace.
        var headerLine =
            "CFTC_Contract_Market_Code,Report_Date_as_YYYY-MM-DD,As_of_Date_In_Form_YYMMDD";
        var columnIndex =
            (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);
        var fieldsWithBlankModernDate = new[] { "001602", "   ", "250115" };

        var modernResult = (string)
            GetFieldMethod.Invoke(
                null,
                [fieldsWithBlankModernDate, columnIndex, "Report_Date_as_YYYY-MM-DD"]
            );

        modernResult.Should().BeNull();
    }

    [Fact]
    public void SplitCsvLine_QuotedFieldWithEmbeddedComma_KeepsFieldIntact()
    {
        // CFTC market names routinely contain commas (e.g. the market name
        // "WHEAT-SRW - CHICAGO BOARD OF TRADE, CBOT"). The split must honour
        // double quotes and keep the comma-bearing field as one cell —
        // otherwise every subsequent column shifts by one, the column-index
        // map mislabels OpenInterest, NonCommLong, etc., and the importer
        // writes silently-mismatched numbers to the COT table. Pin the
        // quoted-comma case so a regression in the inQuotes state machine
        // can't slip in.
        var line = "first,\"two, with comma\",third";

        var fields = (string[])SplitCsvLineMethod.Invoke(null, [line]);

        fields.Should().Equal("first", "two, with comma", "third");
    }
}
