using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Regression for the dropped-filing defect: SEC routinely emits the optional
/// PrimaryDocDescription array shorter than AccessionNumber (trailing empties
/// omitted). The description is a human-readable label, not a required field —
/// a fully-announced filing (accession number, filing date, report date, form,
/// primary document all present) must still be ingested when only its
/// description is missing, with a null Description rather than a dropped row.
/// </summary>
public class SecEdgarClientMapToFilingDataRaggedKeepsAllRowsTests
{
    [Fact]
    public void MapToFilingData_PrimaryDocDescriptionShorterThanAccessionNumbers_KeepsEveryFilingWithNullDescription()
    {
        var asm = typeof(SecEdgarClient).Assembly;
        var recentType = asm.GetType("Equibles.Integrations.Sec.Models.Responses.RecentFilings")!;
        var recent = Activator.CreateInstance(recentType)!;

        void Set(string prop, List<string> values) =>
            recentType.GetProperty(prop)!.SetValue(recent, values);

        // Two announced filings; only the required arrays are full-length.
        // PrimaryDocDescription carries a single entry — the optional label is
        // present for row 0 and absent for row 1.
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

        var result = (List<FilingData>)map.Invoke(null, [recent, "320193"])!;

        // Both filings must survive — the missing description must not gate ingest.
        result.Should().HaveCount(2);
        result
            .Should()
            .Contain(f =>
                f.AccessionNumber == "0000320193-24-000010" && f.Description == "Annual report"
            );
        result
            .Should()
            .Contain(f => f.AccessionNumber == "0000320193-24-000020" && f.Description == null);
    }
}
