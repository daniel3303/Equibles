using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryBuildFilingDataAtFormShortTests
{
    [Fact]
    public void TryBuildFilingDataAt_FormShorterThanAccessionNumber_ReturnsFalseNoThrow()
    {
        // Third arm of TryBuildFilingDataAt's five-arm ragged-payload OR
        // (SecEdgarClient.cs:839-846). FilingDate + ReportDate full-length
        // so the OR short-circuits past their guards and isolates the Form
        // arm specifically. A refactor that drops the Form guard would
        // throw at the unguarded `recent.Form[i]` access on the assignment.
        var assembly = typeof(SecEdgarClient).Assembly;
        var recentFilingsType = assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.RecentFilings"
        );
        var recent = Activator.CreateInstance(recentFilingsType);
        SetList(recent, "AccessionNumber", ["A1", "A2"]);
        SetList(recent, "FilingDate", ["2024-01-01", "2024-01-02"]);
        SetList(recent, "ReportDate", ["2024-01-01", "2024-01-02"]);
        // Form one short — index 1 read would throw without the guard.
        SetList(recent, "Form", ["10-K"]);
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
