using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class CompactNumberTagHelperNegativeNoSignTests
{
    // The four existing CompactNumberTagHelper pins exhaust the
    // sign-prefix matrix (Sign=true: pos/neg/zero arms; Sign=false:
    // positive happy-path). The remaining unexercised cell is
    // (Sign=false, Value<0) — a load-bearing arm in production.
    //
    // CompactNumberTagHelper.Process starts with `var absValue =
    // Math.Abs(Value);` and from then on EVERY downstream
    // computation (data-compact-number, the formatted string,
    // displayPrefix concat) uses the absolute value. With
    // Sign=false, the signPrefix is "" — so a negative input
    // emits its ABSOLUTE value with no minus anywhere in the
    // rendered span. The implicit contract: the helper is for
    // scalar magnitudes; callers wanting signed display opt in
    // via Sign=true.
    //
    // Production trigger: many holdings views display position
    // VALUES (always magnitudes — direction is conveyed by the
    // surrounding "Buy"/"Sell" badge or the green/red row tint,
    // not by the number itself). A repo holding -50_000 shares in
    // a "sold" row should render "$50,000" not "$-50,000" or
    // "-$50,000" — the row context tells the analyst what
    // direction it was.
    //
    // The risks this pin uniquely catches and the four existing
    // pins cannot:
    //
    //   • Dropped Math.Abs — a refactor that read
    //     `formatted = displayPrefix + Value.ToString("N0", ...)`
    //     would compile (decimal supports negative N0 formatting,
    //     yielding "-50,000") and render "$-50,000" — a
    //     minus-sign appearing INSIDE the prefix, breaking the
    //     visual hierarchy on every magnitude display. The
    //     Sign=false-with-POSITIVE-value happy-path pin can't
    //     see this (positive ToString matches absolute ToString).
    //     The Sign=true-NEGATIVE pin uses Sign=true so signPrefix
    //     is "-" already; dropping Math.Abs would render "--$50,000"
    //     (visible failure) but that test asserts the prefix
    //     explicitly so it'd catch a SEPARATE bug shape.
    //
    //   • Implicit-sign regression — a refactor adding "always
    //     show minus when negative, regardless of Sign" — would
    //     render "-$50,000" with Sign=false. The contract says
    //     Sign=false means NO sign indicator; this pin defends
    //     the opt-in semantics.
    //
    //   • data-compact-number attribute sign leak — the
    //     downstream JS reads `data-compact-number` as a Number;
    //     if Math.Abs is dropped, the attribute value becomes
    //     "-50000" and `Number(this.dataset.compactNumber)`
    //     returns -50000, breaking the JS-side "compactify"
    //     formatter that may format negatives differently
    //     (e.g., parentheses, accounting style). Assert the
    //     attribute on the absolute value to pin this too.
    //
    // Pin: Value=-50_000m, Sign=false, Prefix="$". Expected
    // content "$50,000" (NO minus anywhere), data-compact-number
    // "50000", data-compact-prefix "$".
    [Fact]
    public void Process_NegativeValueWithSignDisabled_RendersAbsoluteValueWithNoMinus()
    {
        var sut = new CompactNumberTagHelper
        {
            Value = -50_000m,
            Prefix = "$",
            Sign = false,
        };
        var context = new TagHelperContext(
            new TagHelperAttributeList(),
            new Dictionary<object, object>(),
            "test-id"
        );
        var output = new TagHelperOutput(
            "compactable-number",
            new TagHelperAttributeList(),
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent())
        );

        sut.Process(context, output);

        output.TagName.Should().Be("span");
        output.Content.GetContent().Should().Be("$50,000");
        output.Content.GetContent().Should().NotContain("-");
        output.Attributes["data-compact-number"].Value.Should().Be("50000");
        output.Attributes["data-compact-prefix"].Value.Should().Be("$");
    }
}
