using System.Reflection;
using Equibles.Integrations.Sec;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryBuildFilingDataAtRaggedPayloadTests
{
    // TryBuildFilingDataAt (extracted in #1464) guards SEC's "ragged payload"
    // case where a secondary array (FilingDate, ReportDate, Form,
    // PrimaryDocument, PrimaryDocDescription) is shorter than
    // AccessionNumber. The contract: "skip rows that cannot be fully mapped
    // rather than throw". A refactor that consolidated the per-array Count
    // guards into a single AccessionNumber-only check would compile, pass
    // every existing test (no input today actually exercises this branch),
    // and crash the worker on any ragged response from SEC EDGAR with an
    // IndexOutOfRangeException at the unguarded array access.
    [Fact]
    public void TryBuildFilingDataAt_PrimaryDocDescriptionShorterThanAccessionNumber_ReturnsFalseNoThrow()
    {
        var assembly = typeof(SecEdgarClient).Assembly;
        var recentFilingsType = assembly.GetType(
            "Equibles.Integrations.Sec.Models.Responses.RecentFilings"
        );
        var recent = Activator.CreateInstance(recentFilingsType);
        SetList(recent, "AccessionNumber", ["A1", "A2"]);
        SetList(recent, "FilingDate", ["2024-01-01", "2024-01-02"]);
        SetList(recent, "ReportDate", ["2024-01-01", "2024-01-02"]);
        SetList(recent, "Form", ["10-K", "10-K"]);
        SetList(recent, "PrimaryDocument", ["doc1.htm", "doc2.htm"]);
        // PrimaryDocDescription one short — index 1 read would throw without the guard.
        SetList(recent, "PrimaryDocDescription", ["desc1"]);

        var method = typeof(SecEdgarClient).GetMethod(
            "TryBuildFilingDataAt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { recent, "0000001", 1, null };
        var returned = (bool)method.Invoke(null, args);

        returned.Should().BeFalse();
        args[3].Should().BeNull();
    }

    private static void SetList(object target, string propertyName, List<string> value)
    {
        target.GetType().GetProperty(propertyName).SetValue(target, value);
    }
}
