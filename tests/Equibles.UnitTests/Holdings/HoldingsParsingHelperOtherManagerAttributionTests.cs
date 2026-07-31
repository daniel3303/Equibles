using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsParsingHelperOtherManagerAttributionTests
{
    // OTHERMANAGER is a comma-separated LIST of sequence numbers, and this is the one place both
    // ingest paths now interpret it. Its predecessor read the field with a plain int parse, which
    // rejects every multi-manager attribution — in production that turned ~85% of Berkshire's
    // manager split into "no manager", because Berkshire attributes nearly every position to
    // several managers ("4,8,11").
    //
    // "1,234" is the canonical-ambiguous pin: 1234 under thousands-separator semantics, 1 under
    // list semantics. It catches a refactor that "harmonizes" this parser with the cover-page
    // numeric parsers (where the comma IS a thousands separator) in either direction.
    [Theory]
    [InlineData("2", 2, null)]
    [InlineData("4,8,11", 4, "4,8,11")]
    [InlineData("1,234", 1, "1,234")]
    [InlineData(" 4 , 8 ", 4, "4 , 8")]
    [InlineData("", null, null)]
    [InlineData("   ", null, null)]
    [InlineData(null, null, null)]
    [InlineData("none", null, null)]
    public void ParseOtherManagerAttribution_ListSemantics(
        string raw,
        int? expectedFirst,
        string expectedShared
    )
    {
        var (first, shared) = HoldingsParsingHelper.ParseOtherManagerAttribution(raw);

        first.Should().Be(expectedFirst);
        shared.Should().Be(expectedShared);
    }

    [Fact]
    public void ParseOtherManagerAttribution_SingleManager_IsNotShared()
    {
        // The shared marker must mean "more than one manager", or every ordinary single-manager
        // leg would render with a joint-attribution qualifier that isn't true.
        var (first, shared) = HoldingsParsingHelper.ParseOtherManagerAttribution("7,");

        first.Should().Be(7);
        shared.Should().BeNull("a trailing comma does not make a second manager");
    }

    [Fact]
    public void ParseOtherManagerAttribution_PathologicalList_ClampsToTheColumnBound()
    {
        // The raw list is stored as filed; a filer enumerating dozens of managers must clamp to
        // the column rather than abort the whole batch with a length error.
        var raw = string.Join(',', Enumerable.Range(1, 100));

        var (first, shared) = HoldingsParsingHelper.ParseOtherManagerAttribution(raw);

        first.Should().Be(1);
        shared.Should().HaveLength(HoldingsParsingHelper.SharedManagerNumbersMaxLength);
    }
}
