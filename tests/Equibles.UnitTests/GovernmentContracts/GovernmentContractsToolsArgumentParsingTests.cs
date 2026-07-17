using System.Reflection;
using Equibles.GovernmentContracts.Mcp.Tools;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.GovernmentContracts;

// Contract (audit): malformed dates and inverted ranges must return a correcting
// one-line error, never silently fall back to the default window or the generic
// "no awards found" message — both mislead the caller into "the company won nothing".
// Reflection-invoke since the parsers are private static (same pattern as the
// sibling Shorten surrogate test).
public class GovernmentContractsToolsArgumentParsingTests
{
    private static (DateOnly Start, DateOnly End, string Error) ParseAwardDateRange(
        string startDate,
        string endDate
    )
    {
        var method = typeof(GovernmentContractsTools).GetMethod(
            "ParseAwardDateRange",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return ((DateOnly, DateOnly, string))method!.Invoke(null, [startDate, endDate]);
    }

    private static (bool ByDate, string Error) ParseSortBy(string sortBy)
    {
        var method = typeof(GovernmentContractsTools).GetMethod(
            "ParseSortBy",
            BindingFlags.NonPublic | BindingFlags.Static
        );
        return ((bool, string))method!.Invoke(null, [sortBy]);
    }

    [Fact]
    public void A_valid_range_parses_without_error()
    {
        var (start, end, error) = ParseAwardDateRange("2024-01-01", "2024-12-31");

        error.Should().BeNull();
        start.Should().Be(new DateOnly(2024, 1, 1));
        end.Should().Be(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public void An_inverted_range_returns_an_explicit_error_naming_both_dates()
    {
        var (_, _, error) = ParseAwardDateRange("2026-06-01", "2025-06-01");

        error.Should().Contain("startDate 2026-06-01").And.Contain("endDate 2025-06-01");
    }

    [Theory]
    [InlineData("last year")]
    [InlineData("2026-13-45")]
    [InlineData("01/02/2026")]
    public void A_malformed_start_date_is_rejected_not_silently_defaulted(string startDate)
    {
        var (_, _, error) = ParseAwardDateRange(startDate, null);

        error.Should().Contain($"Unknown startDate '{startDate}'").And.Contain("yyyy-MM-dd");
    }

    [Fact]
    public void A_malformed_end_date_is_rejected_not_silently_defaulted()
    {
        var (_, _, error) = ParseAwardDateRange(null, "yesterday");

        error.Should().Contain("Unknown endDate 'yesterday'").And.Contain("yyyy-MM-dd");
    }

    [Fact]
    public void Omitted_dates_default_to_the_trailing_year_ending_today()
    {
        var (start, end, error) = ParseAwardDateRange(null, null);

        error.Should().BeNull();
        end.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        start.Should().Be(end.AddYears(-1));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("amount", false)]
    [InlineData("AMOUNT", false)]
    [InlineData("date", true)]
    [InlineData(" Date ", true)]
    public void Sort_by_accepts_amount_and_date_case_insensitively(string sortBy, bool byDate)
    {
        var (parsedByDate, error) = ParseSortBy(sortBy);

        error.Should().BeNull();
        parsedByDate.Should().Be(byDate);
    }

    [Fact]
    public void An_unknown_sort_is_rejected_listing_the_accepted_values()
    {
        var (_, error) = ParseSortBy("recent");

        error
            .Should()
            .Contain("Unknown sortBy 'recent'")
            .And.Contain("'amount'")
            .And.Contain("'date'");
    }
}
