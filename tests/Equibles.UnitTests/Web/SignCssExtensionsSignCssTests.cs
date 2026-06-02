using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class SignCssExtensionsSignCssTests
{
    // Contract (the non-inverse direction, for higher-is-better metrics): a positive
    // value colors green ("text-success"), a negative value colors red ("text-error"),
    // and zero is neutral — returning the caller-supplied neutralClass, not a sign color.
    // The mirror of InverseSignCss; a copy-paste that swapped the two, or let zero fall
    // into a sign branch, would mis-color the UI. Zero uses a custom neutralClass so the
    // assertion proves the value is threaded through rather than coincidentally empty.
    [Fact]
    public void SignCss_PositiveNegativeZero_MapsToSuccessErrorAndNeutral()
    {
        5m.SignCss().Should().Be("text-success");
        (-5m).SignCss().Should().Be("text-error");
        0m.SignCss("text-base-content").Should().Be("text-base-content");
    }
}
