using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class SignCssExtensionsNullableNullTests
{
    // Contract: the nullable SignCss overload treats a null as "no sign" → returns the
    // caller's neutralClass, never a sign colour and never an NRE. Every existing SignCss
    // test feeds a concrete value, so the HasValue==false branch (the one a null financial
    // figure — e.g. a missing change % — actually hits in a view) is unexercised. Custom
    // neutralClass proves it's threaded through, not coincidentally the empty default.
    [Fact]
    public void SignCss_NullNullableValue_ReturnsNeutralClass()
    {
        decimal? value = null;

        value.SignCss("text-base-content").Should().Be("text-base-content");
    }
}
