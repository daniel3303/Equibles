using System.Reflection;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class HeadingConversionStepClassifyHeadingTagNoTierTests
{
    // Sibling to ClassifyHeadingTagCascadePrecedenceTests, which pins the
    // precedence within the four-tier cascade. The "no tier matches"
    // fall-through — `return null;` at the bottom of the method — is the
    // contract that says "ordinary body text is NOT a heading". The
    // upstream Execute method then skips the ReplaceNodeWithHeading call
    // and leaves the span as-is.
    //
    // Existing siblings only exercise inputs that satisfy at least one
    // tier (Item+Bold → h2). A refactor that added an unconditional
    // fallback (`return "h6";` or `_ => "p"`) would compile cleanly,
    // pass the precedence sibling (Item still maps to h2), and silently
    // promote every plain-text body span to a heading. The chunker's
    // outline becomes flooded with H6 entries from random body
    // sentences, polluting the document hierarchy used for embedding
    // chunking.
    //
    // Pin: a single plain-text span with no inline styles, no Part /
    // Item prefix, mixed case (so IsAllUppercase is false), no
    // parentheses (so IsApart is false) — classified as null. The
    // text "The quick brown fox" is mixed case + lacking any heading
    // signal in every tier.
    [Fact]
    public void ClassifyHeadingTag_PlainTextMatchingNoTier_ReturnsNull()
    {
        var span = (IElement)
            new HtmlParser()
                .ParseDocument("<html><body><span>The quick brown fox</span></body></html>")
                .QuerySelector("span");

        var method = typeof(HeadingConversionStep).GetMethod(
            "ClassifyHeadingTag",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var step = new HeadingConversionStep();

        var result = (string)
            method.Invoke(step, ["The quick brown fox", new List<IElement> { span }]);

        result.Should().BeNull();
    }
}
