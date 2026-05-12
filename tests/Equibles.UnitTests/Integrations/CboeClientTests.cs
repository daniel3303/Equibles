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
}
