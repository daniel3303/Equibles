using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseVixCsvDateUnparseableTests
{
    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient).GetMethod(
        "ParseVixCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    [Fact]
    public void ParseVixCsv_DateFieldNotMmDdYyyy_SkipsRowDoesNotPersistDefaultDate()
    {
        // Sibling pin to the Open/High/Low/Close arms. ParseVixCsv guards the
        // DATE cell with DateOnly.TryParseExact("MM/dd/yyyy") and `continue`s
        // on failure (CboeClient.cs:329-337) — a structurally distinct arm
        // from the four decimal.TryParse cells. The row below carries five
        // well-formed fields with valid OHLC, so only the date guard can
        // reject it (an ISO "yyyy-MM-dd" date never matches the US exact
        // format). Dropping that guard would leave `date` at default(DateOnly)
        // (0001-01-01) and emit a bogus record, corrupting any time series
        // keyed on the VIX date.
        var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "2020-01-03,13.72,14.49,13.51,14.02\n";

        var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv])!;

        records.Should().BeEmpty();
    }
}
