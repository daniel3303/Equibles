using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

// Contract: ParseAmountRange survives extracted-PDF text that breaks a number's thousands group
// across whitespace/newlines ("$200,\n000"), and never stores a corrupt inverted bracket —
// production held 56 rows like (50001, 200) from a "$50,001 - $200,000" disclosure, which
// downstream surfaced as an impossible range. An unrecoverable upper bound falls back to the
// module's own open-ended convention: (from, from) = "at least $from".
public class DisclosureParsingHelperBrokenAmountTests
{
    [Theory]
    [InlineData("$50,001 - $200,\n000", 50001, 200000)]
    [InlineData("$50,001 - $200, 000", 50001, 200000)]
    [InlineData("$1,000,\n001 - $5,000,000", 1000001, 5000000)]
    public void ParseAmountRange_ThousandsGroupBrokenByWhitespace_RejoinsTheNumber(
        string text,
        long expectedFrom,
        long expectedTo
    )
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange(text);

        Assert.Equal(expectedFrom, from);
        Assert.Equal(expectedTo, to);
    }

    [Fact]
    public void ParseAmountRange_IntactRange_IsUnchanged()
    {
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$50,001 - $200,000");

        Assert.Equal(50001, from);
        Assert.Equal(200000, to);
    }

    [Fact]
    public void ParseAmountRange_CorruptInvertedBracketOnBracketFloor_RederivesCeiling()
    {
        // An upper bound that parsed below the lower bound is source corruption (a mid-number
        // break, or page-break header residue like the "$200?" cap-gains threshold). $50,001
        // is a standard disclosure-bracket floor, so its ceiling is unambiguous — production
        // held 49 rows stored as the impossible ($50,001, $50,001)-style band this way.
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$50,001 - $200junk000");

        Assert.Equal(50001, from);
        Assert.Equal(100000, to);
    }

    [Fact]
    public void ParseAmountRange_CorruptInvertedBracketOffBracketFloor_FallsBackToOpenEndedLowerBound()
    {
        // A lower bound that is NOT a standard bracket floor has no derivable ceiling —
        // "at least $from" stays the honest read, never the impossible pair.
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$1,234 - $200junk000");

        Assert.Equal(1234, from);
        Assert.Equal(1234, to);
    }

    [Fact]
    public void ParseAmountRange_LoneLowerBoundWithTrailingDash_RederivesCeiling()
    {
        // "$50,001 -" is a range whose upper bound was lost to a line/page break: the value
        // is a LOWER bound, and reading it as "up to $50,001" inverts the disclosure.
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$50,001 -");

        Assert.Equal(50001, from);
        Assert.Equal(100000, to);
    }

    [Fact]
    public void ParseAmountRange_TwoSeparateNumbers_NeverMerge()
    {
        // The rejoin only touches the exact comma+whitespace+three-digits shape; a genuine pair
        // of amounts separated by whitespace stays two numbers.
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$1,001 - $15,000");

        Assert.Equal(1001, from);
        Assert.Equal(15000, to);
    }
}
