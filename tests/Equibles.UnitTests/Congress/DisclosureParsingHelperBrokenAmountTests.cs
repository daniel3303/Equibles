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
    public void ParseAmountRange_CorruptInvertedBracket_FallsBackToOpenEndedLowerBound()
    {
        // An upper bound that parsed below the lower bound is unrecoverable source corruption —
        // "at least $from" is the honest read, never the impossible pair.
        var (from, to) = DisclosureParsingHelper.ParseAmountRange("$50,001 - $200junk000");

        Assert.Equal(50001, from);
        Assert.Equal(50001, to);
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
