using Equibles.Web.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Equibles.UnitTests.Web;

public class CompactNumberTagHelperPositiveSignTests
{
    // Third sibling in the Sign-arm family. CompactNumberTagHelper.Process
    // computes `signPrefix` via a nested ternary:
    //   Sign ? Value > 0 ? "+" : Value < 0 ? "-" : "" : ""
    // The four reachable cells of (Sign, sign(Value)) are:
    //   (false, *)   → ""   — pinned by the main happy-path test
    //   (true, neg)  → "-"  — pinned by NegativeSign test
    //   (true, zero) → ""   — pinned by ZeroWithSign test
    //   (true, pos)  → "+"  — THIS pin (the only unpinned cell)
    //
    // The risk this catches uniquely (unreachable from the existing
    // siblings):
    //   • Drop-the-plus regression: a refactor under the false
    //     intuition "positive numbers don't need a sign — the absence
    //     of a minus IS the positive signal" would change `Value > 0
    //     ? "+" : ...` to `Value > 0 ? "" : ...` (or simplify the
    //     whole ternary to `Value < 0 ? "-" : ""`). Result: every
    //     positive change-percent ("+5.2%") renders as just "5.2%" —
    //     visually indistinguishable from cells where Sign=false. The
    //     directional cue an analyst relies on (positive vs. negative
    //     change tinting) silently disappears. The Negative pin
    //     PASSES (-arm still works), the Zero pin PASSES (no prefix
    //     either way), and the default-Sign=false pin PASSES (its
    //     branch is the outer false arm). Only an explicit +arm
    //     assertion catches this.
    //
    //   • +/- swap regression: a copy-paste edit that flipped the two
    //     non-zero arms — `Value > 0 ? "-" : Value < 0 ? "+" : ""` —
    //     would silently invert the sign of every positive AND every
    //     negative change. The Negative pin would FAIL (catches half),
    //     but only this Positive pin catches the other half
    //     independently — proving the assertion fires on the +
    //     arm specifically, not on the - arm spilling its assertion.
    //
    // Production trigger: every change-percent display in the holdings
    // dashboard ("Δ Position", "1D %", "30D %") uses Sign=true. A
    // positive 5.2% must render with the "+" cue so the green colour
    // styling has a textual counterpart for accessibility readers and
    // colour-blind users.
    //
    // Pin: Value = +42_500m with Sign=true, Prefix="$". Expected
    // content "+$42,500", data-compact-prefix "+$". Mirrors the
    // Negative sibling's structure exactly for review symmetry —
    // same input shape, opposite sign, distinct + assertion.
    [Fact]
    public void Process_PositiveValueWithSignEnabled_ShowsPlusBeforePrefix()
    {
        var sut = new CompactNumberTagHelper
        {
            Value = 42_500m,
            Prefix = "$",
            Sign = true,
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
        // SetContent HTML-encodes; `+` (U+002B) is escaped to `&#x2B;` by
        // HtmlEncoder.Default while `-` (U+002D, the negative-sign sibling
        // pin's character) is not — so the rendered HTML still visually
        // shows "+$42,500" to the user, but GetContent returns the encoded
        // form. Asserting the encoded content here pins exactly what the
        // production helper writes to TagHelperOutput.
        output.Content.GetContent().Should().Be("&#x2B;$42,500");
        output.Attributes["data-compact-number"].Value.Should().Be("42500");
        output.Attributes["data-compact-prefix"].Value.Should().Be("+$");
    }
}
