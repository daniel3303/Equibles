using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryBuildFilingDataAtPrimaryDocumentShortTests
{
    [Fact]
    public void TryBuildFilingDataAt_PrimaryDocumentShorterThanAccessionNumber_ReturnsFalseNoThrow()
    {
        // Fourth arm of TryBuildFilingDataAt's five-arm ragged-payload OR
        // (SecEdgarClient.cs:839-846). Closes the family — siblings cover
        // arms 1 (FilingDate), 2 (ReportDate), 3 (Form), and 5
        // (PrimaryDocDescription). FilingDate + ReportDate + Form full-length
        // so the OR short-circuits past those guards and isolates the
        // PrimaryDocument arm. A refactor that drops just this guard would
        // throw at `recent.PrimaryDocument[i]` during the FilingData
        // assignment, taking down the worker on the first ragged response.
        var assembly = typeof(SecEdgarClient).Assembly;
        var recentFilingsType = assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.RecentFilings"
        );
        var recent = Activator.CreateInstance(recentFilingsType);
        SetList(recent, "AccessionNumber", ["A1", "A2"]);
        SetList(recent, "FilingDate", ["2024-01-01", "2024-01-02"]);
        SetList(recent, "ReportDate", ["2024-01-01", "2024-01-02"]);
        SetList(recent, "Form", ["10-K", "10-K"]);
        // PrimaryDocument one short — index 1 read would throw without the guard.
        SetList(recent, "PrimaryDocument", ["doc1.htm"]);
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
