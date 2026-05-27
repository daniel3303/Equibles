using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseVixCsvHighUnparseableTests
{
    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient).GetMethod(
        "ParseVixCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    [Fact]
    public void ParseVixCsv_HighFieldUnparseable_SkipsRowDoesNotPersistDefault()
    {
        // ParseVixCsv has four independent per-cell skip arms — Open, High,
        // Low, Close — each `if (!TryDec(i, ...)) continue;` (CboeClient.cs:
        // 157-164). The existing Culture pin asserts the happy path; the
        // existing skip pins cover date parsing. The individual numeric arms
        // are unpinned. A refactor that drops `if (!TryDec(2, out var high))
        // continue;` (say, "the column is always numeric") would leave `high`
        // at default `0m` from the out var and silently emit a record whose
        // High = 0 — corrupting downstream VIX-chart bars where the
        // intraday high looks like the close went to zero. Pin: a row whose
        // High column is "BAD" must produce NO record (skipped entirely),
        // not a record with High = 0.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "01/02/2004,17.96,BAD,17.54,18.22\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv])!;

        records.Should().BeEmpty();
    }
}
