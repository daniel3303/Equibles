using Equibles.Mcp.Helpers;

namespace Equibles.UnitTests.Mcp;

public class McpOutputTruncationNoteTests
{
    // Contract: nothing was cut off → no note, so callers can append unconditionally.
    [Theory]
    [InlineData(10, 10)]
    [InlineData(10, 5)]
    [InlineData(0, 0)]
    public void TruncationNote_NothingCut_ReturnsEmpty(int shown, int total)
    {
        McpOutput.TruncationNote(shown, total).Should().BeEmpty();
    }

    // Below the cap the note advises raising the cap argument — that advice is actionable.
    [Fact]
    public void TruncationNote_BelowCap_AdvisesRaisingArgument()
    {
        var note = McpOutput.TruncationNote(50, 200);

        note.Should().Contain("Showing first 50 of 200");
        note.Should().Contain("raise maxResults");
    }

    // Contract (#7056): at the cap "raise maxResults" is impossible advice — the argument is
    // already maxed. The note must state the cap and point at a real continuation instead.
    [Fact]
    public void TruncationNote_AtCap_NeverAdvisesRaisingArgument()
    {
        var note = McpOutput.TruncationNote(McpLimit.MaxResults, 1200);

        note.Should().NotContain("raise maxResults");
        note.Should().Contain($"cap of {McpLimit.MaxResults}");
    }

    // Tools with a smaller cap (e.g. the squeeze board's 200) pass it explicitly, and can
    // supply their own at-cap continuation advice.
    [Fact]
    public void TruncationNote_AtCustomCap_UsesSuppliedCapAndAdvice()
    {
        var note = McpOutput.TruncationNote(
            200,
            900,
            cap: 200,
            atCapAdvice: "tighten the liquidity floors"
        );

        note.Should().NotContain("raise maxResults");
        note.Should().Contain("cap of 200");
        note.Should().Contain("tighten the liquidity floors");
    }

    // Contract: the paged note names the ABSOLUTE row range so the model can stitch pages,
    // and always offers a possible continuation — the next offset.
    [Fact]
    public void PagedTruncationNote_MidPages_NamesRangeAndNextOffset()
    {
        var note = McpOutput.PagedTruncationNote(50, 200, 50);

        note.Should().Contain("Showing results 51-100 of 200");
        note.Should().Contain("offset=100");
    }

    // Below the cap the paged note still offers raising the cap argument as the cheap path.
    [Fact]
    public void PagedTruncationNote_BelowCap_AlsoOffersRaisingArgument()
    {
        var note = McpOutput.PagedTruncationNote(50, 200, 0);

        note.Should().Contain("raise maxResults");
        note.Should().Contain("offset=50");
    }

    // At the cap the only continuation offered is the next offset — never "raise maxResults".
    [Fact]
    public void PagedTruncationNote_AtCap_OnlyOffersOffset()
    {
        var note = McpOutput.PagedTruncationNote(McpLimit.MaxResults, 1200, 0);

        note.Should().NotContain("raise maxResults");
        note.Should().Contain($"offset={McpLimit.MaxResults}");
    }

    // The full set from the top → no note; the last page → a closing range statement with no
    // further continuation (offering one would send the caller to an empty page).
    [Fact]
    public void PagedTruncationNote_FullSetFromTop_ReturnsEmpty()
    {
        McpOutput.PagedTruncationNote(30, 30, 0).Should().BeEmpty();
    }

    [Fact]
    public void PagedTruncationNote_LastPage_ClosesWithoutContinuation()
    {
        var note = McpOutput.PagedTruncationNote(20, 70, 50);

        note.Should().Contain("51-70 of 70");
        note.Should().NotContain("offset=70");
        note.Should().NotContain("raise maxResults");
    }

    // An offset past the end must say so — an empty page must never read as "no data exists".
    [Fact]
    public void PagedTruncationNote_OffsetPastEnd_NamesTheOverrun()
    {
        var note = McpOutput.PagedTruncationNote(0, 40, 100);

        note.Should().Contain("offset 100");
        note.Should().Contain("only 40 results exist");
    }

    // Zero rows with no offset is the caller's genuine empty state — not this note's business.
    [Fact]
    public void PagedTruncationNote_EmptyWithoutOffset_ReturnsEmpty()
    {
        McpOutput.PagedTruncationNote(0, 0, 0).Should().BeEmpty();
    }
}
