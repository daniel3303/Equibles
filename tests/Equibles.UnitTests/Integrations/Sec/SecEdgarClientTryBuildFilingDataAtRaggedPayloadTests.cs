using System.Reflection;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.Integrations.Sec;

public class SecEdgarClientTryBuildFilingDataAtRaggedPayloadTests
{
    // TryBuildFilingDataAt (extracted in #1464) guards SEC's "ragged payload"
    // case where a REQUIRED secondary array (FilingDate, ReportDate, Form,
    // PrimaryDocument) is shorter than AccessionNumber: skip rows that cannot
    // be fully mapped rather than throw. PrimaryDocDescription is the one
    // exception — an optional human-readable label SEC routinely emits short.
    // It must NOT gate ingest: when it is short the row is still built with a
    // null Description, indexed defensively so the worker never throws an
    // IndexOutOfRangeException on a ragged response.
    [Fact]
    public void TryBuildFilingDataAt_PrimaryDocDescriptionShorterThanAccessionNumber_BuildsRowWithNullDescription()
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
        // PrimaryDocDescription one short — the optional label is absent for index 1.
        SetList(recent, "PrimaryDocDescription", ["desc1"]);

        var method = typeof(SecEdgarClient).GetMethod(
            "TryBuildFilingDataAt",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var args = new object[] { recent, "0000001", 1, null };
        var returned = (bool)method.Invoke(null, args);

        returned.Should().BeTrue();
        var filing = (FilingData)args[3];
        filing.Should().NotBeNull();
        filing.AccessionNumber.Should().Be("A2");
        filing.Description.Should().BeNull();
    }

    private static void SetList(object target, string propertyName, List<string> value)
    {
        target.GetType().GetProperty(propertyName).SetValue(target, value);
    }
}
