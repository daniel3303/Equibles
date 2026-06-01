using System.ComponentModel.DataAnnotations;
using Equibles.Core.Extensions;

namespace Equibles.UnitTests.Core;

public class EnumExtensionsDisplayWithoutNameTests
{
    // Contract (NameForHumans is a total Enum helper — its `?? enumValue.ToString()`
    // tail proves the intent to always return a usable label): a [Display] attribute
    // that is PRESENT but sets no Name (only Description/Order/etc.) makes
    // DisplayAttribute.GetName() return null. This is a distinct branch from the
    // existing "no attribute at all" pin: there the `?.` short-circuits on a null
    // attribute; here the attribute is non-null and GetName() itself returns null.
    // Both must reach the ToString() fallback. A partial [Display] annotation is a
    // realistic developer mistake, so the helper must yield the member name, not "".
    [Fact]
    public void NameForHumans_DisplayAttributePresentWithoutName_FallsBackToToString()
    {
        WithDescriptionOnly.SomeValue.NameForHumans().Should().Be("SomeValue");
    }

    private enum WithDescriptionOnly
    {
        [Display(Description = "A value annotated for documentation but without a Name")]
        SomeValue,
    }
}
