using System.Reflection;
using Equibles.Integrations.Cftc;

namespace Equibles.UnitTests.Integrations;

public class CftcClientParseLongUnparseableTests
{
    // Contract: ParseLong yields a numeric position value or null when unavailable. Existing pins
    // cover the null-input guard and the thousands-strip success path; the non-null-but-unparseable
    // arm is unexercised. A CFTC cell holding a non-numeric marker like "." (a missing-value glyph
    // that survives GetField's empty→null check) must absorb to null — never throw or coerce to 0.
    [Fact]
    public void ParseLong_NonNumericPlaceholderValue_ReturnsNull()
    {
        var parseLong = typeof(CftcClient).GetMethod(
            "ParseLong",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (long?)parseLong!.Invoke(null, ["."]);

        result.Should().BeNull();
    }
}
