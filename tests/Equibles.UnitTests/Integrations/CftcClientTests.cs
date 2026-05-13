using System.Reflection;
using Equibles.Integrations.Cftc;
using Equibles.Integrations.Cftc.Models;

namespace Equibles.UnitTests.Integrations;

/// <summary>
/// Tests for <see cref="CftcClient"/>. The public entry point downloads ZIPs from
/// cftc.gov, so we exercise the pure-logic private CSV splitter via reflection.
/// </summary>
public class CftcClientTests {
    private static readonly MethodInfo SplitCsvLineMethod = typeof(CftcClient)
        .GetMethod("SplitCsvLine", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ParseLongMethod = typeof(CftcClient)
        .GetMethod("ParseLong", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo BuildColumnIndexMethod = typeof(CftcClient)
        .GetMethod("BuildColumnIndex", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ParseLineMethod = typeof(CftcClient)
        .GetMethod("ParseLine", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo GetFieldMethod = typeof(CftcClient)
        .GetMethod("GetField", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void GetField_ColumnIndexExceedsRowLength_ReturnsNullInsteadOfIndexOutOfRange() {
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
        var headerLine = "Market_and_Exchange_Names,CFTC_Contract_Market_Code,Open_Interest_All,NonComm_Positions_Long_All,Comm_Positions_Long_All,Pct_of_OI_Comm_Short_All";
        var columnIndex = (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);
        var truncatedFields = new[] { "WHEAT-SRW - CHICAGO BOARD OF TRADE", "001602" };

        var result = (string)GetFieldMethod.Invoke(null, [truncatedFields, columnIndex, "Pct_of_OI_Comm_Short_All"]);

        result.Should().BeNull();
    }

    [Fact]
    public void ParseLine_LegacyDateColumnOnly_FallsBackToAsOfDateInFormYyMmDd() {
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
        var columnIndex = (Dictionary<string, int>)BuildColumnIndexMethod.Invoke(null, [headerLine]);
        var line = "001602,250115";

        var record = (CftcReportRecord)ParseLineMethod.Invoke(null, [line, columnIndex]);

        record.Should().NotBeNull();
        record.ReportDate.Should().Be("250115");
        record.ContractMarketCode.Should().Be("001602");
    }

    [Fact]
    public void BuildColumnIndex_QuotedHeaderName_IndexedWithoutQuotes() {
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
    public void ParseLong_ValueWithThousandSeparatorCommas_StripsAndParses() {
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
    public void ParseInt_ValueWithThousandSeparatorCommas_StripsAndParses() {
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
        var parseInt = typeof(CftcClient).GetMethod("ParseInt", BindingFlags.NonPublic | BindingFlags.Static);

        var result = (int?)parseInt!.Invoke(null, ["12,345"]);

        result.Should().Be(12_345);
    }

    [Fact]
    public void SplitCsvLine_QuotedFieldWithEmbeddedComma_KeepsFieldIntact() {
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
