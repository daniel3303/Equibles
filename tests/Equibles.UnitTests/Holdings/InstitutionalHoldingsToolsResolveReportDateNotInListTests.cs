using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling pin to InstitutionalHoldingsToolsResolveReportDateFallbackTests, covering the
/// parseable-but-off-list arms of ResolveReportDateStrict. A date between two reports snaps
/// to the nearest report ON OR BEFORE it (standard as-of semantics — never the newest
/// quarter, which the old helper served silently) and carries a Note naming both dates so
/// the substitution is visible in the tool output; a date OLDER than the tracked history has
/// nothing to snap to and returns an error listing the available dates.
/// </summary>
public class InstitutionalHoldingsToolsResolveReportDateNotInListTests
{
    private static readonly MethodInfo Method = typeof(InstitutionalHoldingsTools).GetMethod(
        "ResolveReportDateStrict",
        BindingFlags.NonPublic | BindingFlags.Static
    )!;

    private static readonly IReadOnlyList<DateOnly> ValidDates =
    [
        new DateOnly(2024, 9, 30),
        new DateOnly(2024, 6, 30),
        new DateOnly(2024, 3, 31),
    ];

    private static (DateOnly Date, string Note, string Error) Resolve(string input) =>
        ((DateOnly, string, string))Method.Invoke(null, [input, ValidDates])!;

    [Fact]
    public void ResolveReportDateStrict_MidQuarterDate_SnapsToNearestReportOnOrBefore()
    {
        // 2024-08-15 sits between the 2024-06-30 and 2024-09-30 reports: the as-of answer is
        // the OLDER report — snapping forward would attribute future data to the past.
        var (date, note, error) = Resolve("2024-08-15");

        date.Should().Be(new DateOnly(2024, 6, 30));
        error.Should().BeNull();
        note.Should().Contain("2024-08-15 is not a 13F report date");
        note.Should().Contain("2024-06-30");
    }

    [Fact]
    public void ResolveReportDateStrict_FutureDate_SnapsToNewestReportWithNote()
    {
        var (date, note, error) = Resolve("2025-01-15");

        date.Should().Be(new DateOnly(2024, 9, 30));
        error.Should().BeNull();
        note.Should().Contain("2025-01-15 is not a 13F report date");
    }

    [Fact]
    public void ResolveReportDateStrict_DateOlderThanHistory_ReturnsErrorListingDates()
    {
        var (_, note, error) = Resolve("2020-03-31");

        note.Should().BeNull();
        error.Should().Contain("No 13F report on or before 2020-03-31");
        error.Should().Contain("2024-09-30");
    }
}
