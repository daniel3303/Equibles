using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseVixCsvLowUnparseableTests
{
    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient).GetMethod(
        "ParseVixCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    [Fact]
    public void ParseVixCsv_LowFieldUnparseable_SkipsRowDoesNotPersistDefault()
    {
        // Third sibling of the OHLC per-cell skip family. With #2337 (High)
        // and #2338 (Open), this isolates the Low arm. Dropping the Low
        // guard would silently emit a record with Low = 0m — making the
        // intraday range (High - Low) look like a full-day crash to zero,
        // which downstream max-drawdown analytics and "circuit-breaker"
        // detectors would flag as a real event.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "01/02/2004,17.96,18.68,BAD,18.22\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv])!;

        records.Should().BeEmpty();
    }
}
