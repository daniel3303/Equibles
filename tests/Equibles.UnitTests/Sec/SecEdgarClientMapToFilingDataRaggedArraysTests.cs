using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial sibling to the column-mapping / culture pins. SEC submissions are
/// six PARALLEL arrays; MapToFilingData drives the loop off AccessionNumber.Count
/// and positionally indexes the other five. The same class's ParseCompaniesFromResponse
/// explicitly guards ragged input ("too short — must skip, not IndexOutOfRange");
/// a caller reasonably relies on the filings parser being equally resilient — a
/// short secondary array must not abort the whole company's filing ingest.
/// </summary>
public class SecEdgarClientMapToFilingDataRaggedArraysTests
{
    [Fact(Skip = "GH-918 — ragged SEC submissions arrays throw, aborting the filing ingest")]
    public void MapToFilingData_SecondaryArrayShorterThanAccessionNumbers_DoesNotThrowAndKeepsMappableRow()
    {
        var asm = typeof(SecEdgarClient).Assembly;
        var recentType = asm.GetType("Equibles.Integrations.Sec.Models.Responses.RecentFilings")!;
        var recent = Activator.CreateInstance(recentType)!;

        void Set(string prop, List<string> values) =>
            recentType.GetProperty(prop)!.SetValue(recent, values);

        // Two filings announced, but PrimaryDocDescription carries only one entry —
        // a ragged payload SEC can emit. Row 0 is fully mappable.
        Set("AccessionNumber", ["0000320193-24-000010", "0000320193-24-000020"]);
        Set("FilingDate", ["2024-02-01", "2024-03-15"]);
        Set("ReportDate", ["2023-12-30", "2024-01-31"]);
        Set("Form", ["10-K", "8-K"]);
        Set("PrimaryDocument", ["aapl-20231230.htm", "ex991.htm"]);
        Set("PrimaryDocDescription", ["Annual report"]);

        var map = typeof(SecEdgarClient).GetMethod(
            "MapToFilingData",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        List<FilingData> Invoke()
        {
            try
            {
                return (List<FilingData>)map.Invoke(null, [recent, "320193"])!;
            }
            catch (TargetInvocationException ex)
            {
                throw ex.InnerException!;
            }
        }

        var act = Invoke;

        act.Should()
            .NotThrow(
                "a ragged SEC submissions payload must not abort the company's filing ingest"
            );
        Invoke().Should().Contain(f => f.AccessionNumber == "0000320193-24-000010");
    }
}
