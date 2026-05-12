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
