using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

// The VIX CSV column layout differs from all other put/call CSVs:
// VIX:   Date, Ratio, PutVol, CallVol, TotalVol
// Other: Date, CallVol, PutVol, TotalVol, Ratio
// ParsePutCallCsv must swap indices when csvType is Vix.
public class CboeClientVixPutCallColumnOrderTests
{
    private static readonly MethodInfo ParsePutCallCsvMethod = typeof(CboeClient).GetMethod(
        "ParsePutCallCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void ParsePutCallCsv_VixCsvType_MapsRatioFromFieldOneNotFieldFour()
    {
        var csv =
            "Date,VIX Put/Call Ratio,VIX Put Volume,VIX Call Volume,Total VIX Options Volume\n"
            + "01/02/2020,1.18,5095,4328,9423\n";

        var records =
            (List<CboePutCallRecord>)
                ParsePutCallCsvMethod.Invoke(null, [csv, CboePutCallCsvType.Vix]);

        records.Should().ContainSingle();
        var record = records[0];
        record.Date.Should().Be(new DateOnly(2020, 1, 2));
        record.PutCallRatio.Should().Be(1.18m);
        record.PutVolume.Should().Be(5095);
        record.CallVolume.Should().Be(4328);
        record.TotalVolume.Should().Be(9423);
    }
}
