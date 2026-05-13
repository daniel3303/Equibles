using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

/// <summary>
/// Tests for <see cref="CboeClient"/>. The public entry points hit cdn.cboe.com,
/// so we exercise the pure-logic private CSV parser via reflection.
/// </summary>
public class CboeClientTests {
    private static readonly MethodInfo ParsePutCallCsvMethod = typeof(CboeClient)
        .GetMethod("ParsePutCallCsv", BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient)
        .GetMethod("ParseVixCsv", BindingFlags.NonPublic | BindingFlags.Static);

    [Fact]
    public void ParsePutCallCsv_RowWithUnparseableDate_IsSkipped() {
        // The CBOE CSV occasionally carries narrative rows ("Disclaimer:...") between
        // the header and real data. ParsePutCallCsv must skip any row whose first
        // field doesn't match the MM/dd/yyyy exact-format date. If a regression
        // loosened TryParseExact (e.g. switched to TryParse), narrative rows would
        // be parsed as DateTime.MinValue and silently flood the database with junk
        // ratio records. Pin the skip path on a non-date row.
        var csv = "Date,Call Volume,Put Volume,Total Volume,P/C Ratio\n" +
                  "Disclaimer: data provided by CBOE,,,,\n" +
                  "01/15/2025,100000,80000,200000,0.80\n";

        var records = (List<CboePutCallRecord>)ParsePutCallCsvMethod.Invoke(null, [csv]);

        records.Should().ContainSingle()
            .Which.Date.Should().Be(new DateOnly(2025, 1, 15));
    }

    [Fact]
    public void ParsePutCallCsv_RowWithFewerThanFiveFields_IsSkippedWithoutThrowing() {
        // The CBOE put/call CSV expects rows of the form
        //   Date,CallVolume,PutVolume,TotalVolume,P/C Ratio
        // Real CBOE feeds occasionally emit truncated rows during data outages
        // or partial-publish windows (e.g. only the first 1-2 columns populated
        // before the rest is written). ParsePutCallCsv guards every row with
        //   if (fields.Length < 5) continue;
        // BEFORE indexing into fields[1..4]. The guard is load-bearing: without
        // it, a 2-field row like "01/15/2025,100" would throw IndexOutOfRange
        // on fields[2] / fields[3] / fields[4], abort the foreach inside the
        // public DownloadPutCallRatios, and crash the entire ingest cycle —
        // not just lose this one row but blow up the worker pass that was
        // about to import dozens of days of legitimate ratio history that
        // appeared LATER in the same CSV.
        //
        // The existing `ParsePutCallCsv_RowWithUnparseableDate_IsSkipped` pin
        // exercises the date-parse skip path on a 5-field row (the disclaimer
        // line has the right column count). The fields.Length < 5 branch is
        // structurally distinct — it fires BEFORE the date parse, on rows the
        // disclaimer test never reaches. The pair (date-skip on full row,
        // length-skip on short row) covers both early-exit guards in the
        // parser's per-row sanity check.
        //
        // Pin: a 2-field row sandwiched between header and a valid 5-field
        // row. The valid row is parsed, the short row is skipped silently,
        // and no exception escapes ParsePutCallCsv. The single-record
        // assertion on the valid date proves the parser CONTINUED past the
        // malformed row instead of aborting.
        var csv = "Date,Call Volume,Put Volume,Total Volume,P/C Ratio\n" +
                  "01/15/2025,100\n" +
                  "01/16/2025,100000,80000,200000,0.80\n";

        var records = (List<CboePutCallRecord>)ParsePutCallCsvMethod.Invoke(null, [csv]);

        records.Should().ContainSingle()
            .Which.Date.Should().Be(new DateOnly(2025, 1, 16));
    }

    [Fact]
    public void ParseVixCsv_RowWithFewerThanFiveFields_IsSkippedWithoutThrowing() {
        // Sibling to ParsePutCallCsv_RowWithFewerThanFiveFields_IsSkippedWithoutThrowing.
        // Same defensive pattern — `if (fields.Length < 5) continue;` — but in a
        // structurally distinct parser. The VIX history CSV occasionally emits
        // truncated rows during partial-publish windows: CBOE's CDN serves the
        // file mid-write when the upstream pipeline updates daily OHLC
        // intraday, and a row may carry only the date column before the OHLC
        // values are populated.
        //
        // Without the guard, a 2-field row like "01/02/2020,13.46" would throw
        // IndexOutOfRange on fields[2], fields[3], or fields[4] (the High/Low/
        // Close decimal extractions), aborting the foreach inside the public
        // DownloadVixHistory and crashing the entire VIX ingest pass. The
        // existing ParseVixCsv_RowWithUnparseableOhlcDecimal pin runs on a
        // 5-field row (decimal parse fails), so it doesn't exercise the
        // length-skip branch — those are independent guards.
        //
        // Pin: a 2-field row sandwiched between header and a valid 5-field
        // row. The valid row parses, the short row is silently skipped, no
        // exception escapes. Single-record assertion proves the parser
        // CONTINUED past the malformed row rather than aborting.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" +
                  "01/02/2020,13.46\n" +
                  "01/03/2020,13.72,14.49,13.51,14.02\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv]);

        records.Should().ContainSingle()
            .Which.Date.Should().Be(new DateOnly(2020, 1, 3));
    }

    [Fact]
    public void ParseLong_ValueWithEmbeddedThousandsSeparators_ReturnsParsedLongWithoutCommas() {
        // ParseLong (used by ParsePutCallCsv for Call/Put/TotalVolume columns)
        // strips commas BEFORE long.TryParse:
        //   `long.TryParse(value.Replace(",", ""), InvariantCulture, ...)`
        // The Replace is load-bearing defensive code that's easy to lose
        // in a refactor — and the loss is silent:
        //
        // - A "clean up" that drops the Replace (under the assumption that
        //   CultureInfo.InvariantCulture's number parser handles thousands
        //   separators) compiles cleanly. But InvariantCulture's default
        //   NumberStyles.Integer does NOT accept thousands separators —
        //   that requires NumberStyles.AllowThousands, which TryParse's
        //   single-arg overload doesn't set.
        // - Without the Replace, every "1,234,567"-formatted value returns
        //   null instead of 1234567. Rows still import (bare ratio and date
        //   parse fine), but volume columns silently appear empty in
        //   put/call analytics. Worst-case observability: no exception,
        //   no log line, downstream consumers see "missing 2024 volume
        //   data" without an error trail to investigate.
        // - The same Replace pattern is NOT present in ParseDecimal — that
        //   asymmetry suggests Replace was added specifically because the
        //   CBOE volume feed emits comma-formatted values at some point in
        //   its history. Pinning the behavior locks in that domain
        //   assumption.
        //
        // Pin: invoke ParseLong directly via reflection on a comma-formatted
        // input. The assertion that the result equals 1_234_567 only holds
        // if Replace stripped the commas before TryParse — without it the
        // method returns null (TryParse fails on "1,234,567"). This
        // exercises the comma-strip in isolation, independent of any CSV
        // tokenization concerns. The existing parse-skip pins exercise the
        // CSV path; this pin protects the inner ParseLong contract that
        // ParsePutCallCsv depends on.
        var parseLong = typeof(CboeClient).GetMethod("ParseLong", BindingFlags.NonPublic | BindingFlags.Static);

        var result = (long?)parseLong!.Invoke(null, ["1,234,567"]);

        result.Should().Be(1_234_567L);
    }

    [Fact]
    public void ParseVixCsv_RowWithUnparseableOhlcDecimal_IsSkipped() {
        // The CBOE VIX history CSV occasionally carries rows where one of the
        // OHLC columns is blank or "N/A" (early history before VIX listed
        // intraday open/high/low — only close was published). ParseVixCsv
        // walks decimal.TryParse for Open/High/Low/Close and must skip the
        // entire row if any of the four fails, otherwise an unparseable Open
        // would leave a default-zero OHLC row in the VIX history table and
        // silently corrupt volatility analytics. The unique branch here is
        // not the date skip (already covered by ParsePutCallCsv's test) but
        // the decimal-skip fall-through — pin it on a row whose High column
        // is non-numeric while the date is valid, and assert the next valid
        // row still parses so we know we hit `continue` and not `return`.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" +
                  "01/02/2020,13.46,N/A,13.20,12.47\n" +
                  "01/03/2020,13.72,14.49,13.51,14.02\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv]);

        records.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new CboeVixRecord {
                Date = new DateOnly(2020, 1, 3),
                Open = 13.72m,
                High = 14.49m,
                Low = 13.51m,
                Close = 14.02m
            });
    }
}
