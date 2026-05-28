using Equibles.Web.Extensions;

namespace Equibles.UnitTests.Web;

public class SignCssExtensionsInverseSignCssTests
{
    [Fact]
    public void InverseSignCss_PositiveValue_ReturnsErrorNotSuccess()
    {
        // "Inverse" is for lower-is-better metrics (short interest, days-to-cover):
        // a positive value is bad and must color red. A copy-paste from SignCss that
        // forgot to swap would return "text-success" here, mis-coloring the UI.
        5m.InverseSignCss().Should().Be("text-error");
    }
}
