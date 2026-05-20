using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class ProfileFormattingJoinLocationWhitespaceTests
{
    // Contract derived from class doc ("Small presentation helpers shared by
    // the entity profile pages") and the method name `JoinLocation` — it joins
    // location parts and must NOT emit a stray leading or trailing comma when
    // one part is missing. Whitespace-only inputs (typically a database field
    // that was set to " " or " " rather than NULL) are real in EDGAR /
    // institutional-holder data — the implementation uses `IsNullOrWhiteSpace`
    // to filter them out. A refactor that switches the predicate to
    // `IsNullOrEmpty` would silently re-emit the trailing ", " in the rendered
    // profile.
    [Fact]
    public void JoinLocation_WhitespaceOnlyStateOrCountry_IsOmittedFromOutput()
    {
        var result = ProfileFormatting.JoinLocation("San Francisco", "  ");

        result.Should().Be("San Francisco");
    }
}
