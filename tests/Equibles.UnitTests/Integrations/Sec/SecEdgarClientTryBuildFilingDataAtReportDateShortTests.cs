using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryBuildFilingDataAtReportDateShortTests
{
    [Fact]
    public void TryBuildFilingDataAt_ReportDateShorterThanAccessionNumber_ReturnsFalseNoThrow()
    {
        // Second arm of TryBuildFilingDataAt's five-arm ragged-payload OR
        // (SecEdgarClient.cs:839-846). With FilingDate full-length, the OR
        // short-circuits PAST the FilingDate guard so this test isolates the
        // ReportDate arm specifically — pinning it independently of its
        // siblings. A refactor that drops just the ReportDate guard (e.g.
        // collapsing "FilingDate + ReportDate" into one) would still throw
        // at `recent.ReportDate[i]` inside ParseInvariantDateOr.
        var assembly = typeof(SecEdgarClient).Assembly;
        var recentFilingsType = assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.RecentFilings"
        );
        var recent = Activator.CreateInstance(recentFilingsType);
        SetList(recent, "AccessionNumber", ["A1", "A2"]);
        SetList(recent, "FilingDate", ["2024-01-01", "2024-01-02"]);
        // ReportDate one short — index 1 read would throw without the guard.
        SetList(recent, "ReportDate", ["2024-01-01"]);
        SetList(recent, "Form", ["10-K", "10-K"]);
        SetList(recent, "PrimaryDocument", ["doc1.htm", "doc2.htm"]);
        SetList(recent, "PrimaryDocDescription", ["desc1", "desc2"]);

        var method = typeof(SecEdgarClient).GetMethod(
            "TryBuildFilingDataAt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { recent, "0000001", 1, null };
        var returned = (bool)method!.Invoke(null, args);

        returned.Should().BeFalse();
        args[3].Should().BeNull();
    }

    private static void SetList(object target, string propertyName, List<string> value)
    {
        target!.GetType().GetProperty(propertyName)!.SetValue(target, value);
    }
}
