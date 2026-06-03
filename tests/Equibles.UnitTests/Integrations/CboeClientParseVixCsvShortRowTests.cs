using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseVixCsvShortRowTests
{
    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient).GetMethod(
        "ParseVixCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    [Fact]
    public void ParseVixCsv_TruncatedRowWithTooFewColumns_SkippedWithoutThrowing()
    {
        // The OHLC per-cell skip family pins bad VALUES in full-width rows; this pins the
        // upstream short-row guard (fields.Length < minFields). A truncated line in the
        // untrusted CBOE CSV must be skipped, not indexed into — without the guard the OHLC
        // accessors (fields[3]/fields[4]) throw IndexOutOfRange and abort the whole parse.
        var csv =
            "DATE,OPEN,HIGH,LOW,CLOSE\n"
            + "01/02/2004,17.96,18.68\n" // only 3 fields — must be skipped, not crash
            + "01/05/2004,18.22,18.30,17.80,18.00\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv])!;

        records.Should().ContainSingle();
        records[0].Date.Should().Be(new DateOnly(2004, 1, 5));
        records[0].Close.Should().Be(18.00m);
    }
}
