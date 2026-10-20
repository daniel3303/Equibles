using Equibles.Core.Documents;
using Xunit;

namespace Equibles.UnitTests.Esef;

/// <summary>
/// Pins the streaming pre-check in <see cref="XhtmlCompatibility.ExpandEmptyElements"/>:
/// the XDocument only has to be built when the document contains an empty, non-void
/// XHTML element. Everything else must come back untouched - and <see cref="Assert.Same"/>
/// is the assertion that proves it, because returning the same instance means no
/// re-serialisation happened, which is the whole point of the change.
/// </summary>
public class XhtmlCompatibilitySkipDomTests
{
    [Fact]
    public void VoidOnlyEmptyElements_ReturnTheSameInstance()
    {
        // This document contains "/>" (so it enters the method) but every empty
        // element is a void element, which the expansion must leave alone. Before
        // the pre-check this still loaded and re-serialised the whole document.
        var source =
            "<html xmlns=\"http://www.w3.org/1999/xhtml\">"
            + "<head><meta charset=\"utf-8\"/><link rel=\"stylesheet\" href=\"a.css\"/></head>"
            + "<body><p>Text</p><br/></body></html>";

        var result = XhtmlCompatibility.ExpandEmptyElements(source);

        Assert.Same(source, result);
    }

    [Fact]
    public void EmptyNonVoidElement_IsStillExpanded()
    {
        // The case the method exists for: <title/> would swallow the rest of the
        // report under HTML rules, so it must become <title></title>.
        var source =
            "<html xmlns=\"http://www.w3.org/1999/xhtml\">"
            + "<head><title/></head><body><p>Text</p></body></html>";

        var result = XhtmlCompatibility.ExpandEmptyElements(source);

        Assert.NotSame(source, result);
        Assert.Contains("<title></title>", result, StringComparison.Ordinal);
        Assert.DoesNotContain("<title/>", result, StringComparison.Ordinal);
    }
}
