using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseVixCsvCloseUnparseableTests
{
    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient).GetMethod(
        "ParseVixCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    [Fact]
    public void ParseVixCsv_CloseFieldUnparseable_SkipsRowDoesNotPersistDefault()
    {
        // Final sibling — closes the OHLC per-cell skip family (Open #2338,
        // High #2337, Low #2339). With all four guards individually pinned,
        // a refactor that drops any single guard surfaces on its own test
        // instead of slipping past on short-circuit. Close is the most
        // load-bearing of the four: every downstream VIX consumer uses
        // closing values as the daily reference, so a record with Close = 0
        // would silently wreck daily-return calculations and any "is VIX
        // above X" alert that compares end-of-day levels.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "01/02/2004,17.96,18.68,17.54,BAD\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv])!;

        records.Should().BeEmpty();
    }
}
