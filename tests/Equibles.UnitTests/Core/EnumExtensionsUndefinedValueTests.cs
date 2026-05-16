using Equibles.Core.Extensions;
using Equibles.Fred.Data.Models;

namespace Equibles.UnitTests.Core;

public class EnumExtensionsUndefinedValueTests
{
    [Fact]
    public void NameForHumans_UndefinedEnumValue_FallsBackToToStringInsteadOfThrowing()
    {
        // Contract: NameForHumans is a total Enum helper — its own `?? enumValue.ToString()`
        // fallback proves the intent to never fail. Undefined values are legal C# enums
        // (any cast int), so it must fall back to ToString(), not throw.
        var undefined = (FredSeriesCategory)999;

        var result = undefined.NameForHumans();

        result
            .Should()
            .Be(
                "999",
                "an undefined enum value has no member to reflect, so NameForHumans "
                    + "must reach its ToString() fallback rather than First()-throwing"
            );
    }
}
