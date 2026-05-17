using System.Globalization;
using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientMapToFilingDataCultureTests
{
    // Contract: MapToFilingData maps the SEC submissions feed's ISO (yyyy-MM-dd)
    // filingDate/reportDate to the FilingData row. That mapping must be
    // culture-independent: ar-SA defaults to the Umm al-Qura (Hijri) calendar,
    // where culture-sensitive DateOnly.TryParse fails on an ISO string — a
    // Worker there would stamp every SEC filing with DateOnly.MinValue.
    [Fact]
    public void MapToFilingData_IsoDatesUnderHijriCulture_MapsToGregorianDates()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var asm = typeof(SecEdgarClient).Assembly;
            var recentType = asm.GetType(
                "Equibles.Integrations.Sec.Models.Responses.RecentFilings"
            )!;
            var recent = Activator.CreateInstance(recentType)!;
            void Set(string prop, List<string> values) =>
                recentType.GetProperty(prop)!.SetValue(recent, values);

            Set("AccessionNumber", ["0000320193-24-000010"]);
            Set("FilingDate", ["2024-02-01"]);
            Set("ReportDate", ["2023-12-30"]);
            Set("Form", ["10-K"]);
            Set("PrimaryDocument", ["aapl-20231230.htm"]);
            Set("PrimaryDocDescription", ["Annual report"]);

            var map = typeof(SecEdgarClient).GetMethod(
                "MapToFilingData",
                BindingFlags.NonPublic | BindingFlags.Static
            )!;

            var result = (List<FilingData>)map.Invoke(null, [recent, "320193"])!;

            result.Should().HaveCount(1);
            result[0].FilingDate.Should().Be(new DateOnly(2024, 2, 1));
            result[0].ReportDate.Should().Be(new DateOnly(2023, 12, 30));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
