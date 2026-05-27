using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryBuildFilingDataAtFilingDateShortTests
{
    [Fact]
    public void TryBuildFilingDataAt_FilingDateShorterThanAccessionNumber_ReturnsFalseNoThrow()
    {
        // TryBuildFilingDataAt's ragged-payload guard is a five-arm OR over
        // FilingDate / ReportDate / Form / PrimaryDocument / PrimaryDocDescription
        // (SecEdgarClient.cs:839-846). The existing pin only triggers the LAST
        // arm (PrimaryDocDescription short) — short-circuit evaluation means a
        // refactor that drops the FilingDate guard while keeping the others
        // would still pass that test, then crash the worker the first time
        // SEC returns a payload with FilingDate one short of AccessionNumber.
        // Pin: only FilingDate is short → must return false (not throw on the
        // unguarded `recent.FilingDate[1]` access inside ParseInvariantDateOr).
        var assembly = typeof(SecEdgarClient).Assembly;
        var recentFilingsType = assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.RecentFilings"
        );
        var recent = Activator.CreateInstance(recentFilingsType);
        SetList(recent, "AccessionNumber", ["A1", "A2"]);
        // FilingDate one short — index 1 read would throw without the guard.
        SetList(recent, "FilingDate", ["2024-01-01"]);
        SetList(recent, "ReportDate", ["2024-01-01", "2024-01-02"]);
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
