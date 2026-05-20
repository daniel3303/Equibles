using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Sibling pin to InstitutionalHoldingsToolsResolveReportDateFallbackTests, which
/// covers the "input does not parse" fallback arm. This pin covers the OTHER
/// fallback arm: input PARSES cleanly but is not present in the holder's
/// validDates list — the helper must still fall back to validDates[0] rather
/// than echoing back a date the holder never filed for.
/// </summary>
public class InstitutionalHoldingsToolsResolveReportDateNotInListTests
{
    [Fact]
    public void ResolveReportDate_InputParsesButNotInValidDates_FallsBackToMostRecent()
    {
        // ResolveReportDate's ternary is
        //   TryParseReportDate(input, out var parsed) && validDates.Contains(parsed)
        //     ? parsed
        //     : validDates[0];
        // The Contains() conjunct is the load-bearing validation: every MCP
        // tool caller hands an LLM-supplied date string into ResolveReportDate
        // and uses the result as a SQL filter on InstitutionalHolding rows.
        // Without the Contains() guard, a syntactically valid date that does
        // NOT correspond to a real 13F filing (e.g. "2025-01-15" on a holder
        // whose report dates are 2024-09-30 / 2024-06-30 / 2024-03-31) would
        // pass straight through to the WHERE clause and silently return an
        // empty result set — indistinguishable from "the holder filed nothing
        // that quarter" but actually "the date doesn't exist for this holder".
        // The existing fallback pin covers the unparseable-input arm; nothing
        // covers the parseable-but-not-in-list arm.
        //
        // The risk: a refactor that "tidies" the ternary to
        //   TryParseReportDate(input, out var parsed) ? parsed : validDates[0];
        // — perhaps because the Contains() looks like a redundant double-check
        // when validDates is already the holder's filings — would compile,
        // pass every existing pin (unparseable fallback, happy-path callers
        // that pass null / empty / a real filed date), and silently corrupt
        // the result set semantics on any LLM that ever guesses a near-but-
        // not-exact date.
        //
        // Pin: feed a parseable date that is NOT in validDates and assert
        // the helper falls back to validDates[0] (the most recent filing,
        // since callers materialize OrderByDescending).
        var method = typeof(InstitutionalHoldingsTools).GetMethod(
            "ResolveReportDate",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        IReadOnlyList<DateOnly> validDates =
        [
            new DateOnly(2024, 9, 30),
            new DateOnly(2024, 6, 30),
            new DateOnly(2024, 3, 31),
        ];

        var result = (DateOnly)method.Invoke(null, ["2025-01-15", validDates]);

        result.Should().Be(new DateOnly(2024, 9, 30));
    }
}
