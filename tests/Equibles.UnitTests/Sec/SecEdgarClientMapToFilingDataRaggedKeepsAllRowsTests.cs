using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Sec;

public class SecEdgarClientMapToFilingDataRaggedKeepsAllRowsTests
{
    [Fact(
        Skip = "GH-3323 — a valid filing is dropped when only its primaryDocDescription is missing"
    )]
    public void MapToFilingData_SecondaryArrayShorter_StillEmitsEveryAnnouncedFiling()
    {
        // Contract: the loop drives off AccessionNumber.Count and positionally indexes the five
        // parallel arrays. "Not aborting on ragged input" must mean EVERY announced filing is
        // ingested — a missing optional secondary field (here PrimaryDocDescription[1]) yields a
        // null for that field, not a dropped filing. The sibling test only asserts row 0 survives;
        // it can't catch a regression that silently drops the ragged row 1.
        var asm = typeof(SecEdgarClient).Assembly;
        var recentType = asm.GetType("Equibles.Integrations.Sec.Models.Responses.RecentFilings")!;
        var recent = Activator.CreateInstance(recentType)!;

        void Set(string prop, List<string> values) =>
            recentType.GetProperty(prop)!.SetValue(recent, values);

        Set("AccessionNumber", ["0000320193-24-000010", "0000320193-24-000020"]);
        Set("FilingDate", ["2024-02-01", "2024-03-15"]);
        Set("ReportDate", ["2023-12-30", "2024-01-31"]);
        Set("Form", ["10-K", "8-K"]);
        Set("PrimaryDocument", ["aapl-20231230.htm", "ex991.htm"]);
        Set("PrimaryDocDescription", ["Annual report"]); // ragged: only one entry for two filings

        var map = typeof(SecEdgarClient).GetMethod(
            "MapToFilingData",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        List<FilingData> result;
        try
        {
            result = (List<FilingData>)map.Invoke(null, [recent, "320193"])!;
        }
        catch (TargetInvocationException ex)
        {
            throw ex.InnerException!;
        }

        result
            .Select(f => f.AccessionNumber)
            .Should()
            .Contain(["0000320193-24-000010", "0000320193-24-000020"]);
    }
}
