using System.Globalization;
using System.Reflection;
using Equibles.Integrations.Cboe;
using Equibles.Integrations.Cboe.Models;

namespace Equibles.UnitTests.Integrations;

public class CboeClientParseVixCsvCultureTests
{
    private static readonly MethodInfo ParseVixCsvMethod = typeof(CboeClient).GetMethod(
        "ParseVixCsv",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    [Fact]
    public void ParseVixCsv_FullyPopulatedRowUnderCommaDecimalCulture_MapsOhlcCultureIndependently()
    {
        // VIX history is US-formatted: MM/dd/yyyy dates, dot-decimal OHLC. The
        // parser pins this with InvariantCulture; the existing VIX pins only
        // cover skip paths, never the mapping nor that the InvariantCulture
        // guarantee actually holds. Under de-DE (comma decimal, dd.MM dates) a
        // regression dropping InvariantCulture would misparse 17.96 -> 1796 or
        // reject the date — invisible on a US dev box, broken in production.
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var csv = "DATE,OPEN,HIGH,LOW,CLOSE\n" + "01/02/2004,17.96,18.68,17.54,18.22\n";

            var records = (List<CboeVixRecord>)ParseVixCsvMethod.Invoke(null, [csv]);

            var record = records.Should().ContainSingle().Subject;
            record.Date.Should().Be(new DateOnly(2004, 1, 2));
            record.Open.Should().Be(17.96m);
            record.High.Should().Be(18.68m);
            record.Low.Should().Be(17.54m);
            record.Close.Should().Be(18.22m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
