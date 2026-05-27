using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseVixCsvOpenUnparseableTests
{
    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient).GetMethod(
        "ParseVixCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    [Fact]
    public void ParseVixCsv_OpenFieldUnparseable_SkipsRowDoesNotPersistDefault()
    {
        // Sibling pin to the High arm (#2337). ParseVixCsv has four
        // independent per-cell skip arms (Open, High, Low, Close) — each is a
        // separate `if (!TryDec(i, ...)) continue;` (CboeClient.cs:157-164).
        // The High pin demonstrated the shape; this isolates the Open arm.
        // A refactor that drops the Open guard would leave `open` at the
        // out-var default `0m` and silently emit a record with Open = 0 —
        // any "gap up/down" detector reading consecutive Open-vs-prior-Close
        // would see a massive overnight gap and misfire alerting.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "01/02/2004,BAD,18.68,17.54,18.22\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv])!;

        records.Should().BeEmpty();
    }
}
