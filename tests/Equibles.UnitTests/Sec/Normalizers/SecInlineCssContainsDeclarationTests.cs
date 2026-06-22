using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class SecInlineCssContainsDeclarationTests
{
    // Contract (from the doc-comment): SEC EDGAR emits inline CSS both with and without a
    // space after the colon ("text-align:center" and "text-align: center"), and
    // ContainsDeclaration must detect *both* forms. The spaced form is the non-obvious half —
    // a naive source.Contains("text-align:center") would miss it, silently breaking the
    // center-alignment / bold / list-wrapper detection that drives heading and list
    // normalization across every filing that emits the spaced form. This pins that guarantee.
    [Fact]
    public void ContainsDeclaration_SpacedAndUnspacedColonForms_BothDetected()
    {
        var unspaced = SecInlineCss.ContainsDeclaration(
            "text-align:center",
            "text-align",
            "center"
        );
        var spaced = SecInlineCss.ContainsDeclaration("text-align: center", "text-align", "center");

        unspaced.Should().BeTrue("EDGAR emits the unspaced 'property:value' form");
        spaced
            .Should()
            .BeTrue(
                "EDGAR also emits the spaced 'property: value' form, which a single Contains would miss"
            );
    }
}
