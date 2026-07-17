using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Pins the strict-resolution contract of ResolveReportDateStrict, which replaced the old
/// silent-fallback ResolveReportDate (any bad input → validDates[0]) after the MCP audit
/// showed an LLM asking for a historical quarter could receive the LATEST quarter's data and
/// present it as historical. Contract (validDates is newest-first): a null/blank input keeps
/// the documented "defaults to latest" behavior with no note; an unparseable input returns a
/// one-line error naming the format and listing the available dates — never a date.
/// </summary>
public class InstitutionalHoldingsToolsResolveReportDateFallbackTests
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
    public void ResolveReportDateStrict_NullInput_ReturnsMostRecentDateWithoutNote()
    {
        var (date, note, error) = Resolve(null);

        date.Should().Be(new DateOnly(2024, 9, 30));
        note.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void ResolveReportDateStrict_ExactMatch_ReturnsThatDateWithoutNote()
    {
        var (date, note, error) = Resolve("2024-06-30");

        date.Should().Be(new DateOnly(2024, 6, 30));
        note.Should().BeNull();
        error.Should().BeNull();
    }

    [Fact]
    public void ResolveReportDateStrict_UnparseableInput_ReturnsErrorListingAvailableDates()
    {
        var (_, note, error) = Resolve("not-a-date");

        note.Should().BeNull();
        error.Should().Contain("Could not parse reportDate 'not-a-date'");
        error.Should().Contain("YYYY-MM-DD");
        error.Should().Contain("2024-09-30");
    }
}
