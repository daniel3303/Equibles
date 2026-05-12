using System.Reflection;
using Equibles.Integrations.Cftc;

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
