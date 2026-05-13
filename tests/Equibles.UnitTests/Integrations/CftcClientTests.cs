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
