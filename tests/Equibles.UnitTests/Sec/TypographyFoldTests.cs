using Equibles.Sec.Data.Models;
using Equibles.Sec.Mcp.Tools;

namespace Equibles.UnitTests.Sec;

// TypographyFold backs SearchDocumentKeyword's matching: filings store smart punctuation
// (U+2019 apostrophes, curly quotes, en/em dashes) while callers type ASCII, so both the
// keyword and the searched line are folded before the ordinal comparison. The 1:1 length
// invariant is load-bearing — HighlightKeyword maps folded match indices back onto the
// ORIGINAL line to bold it, which breaks the moment folding changes string length.
public class TypographyFoldTests
{
    [Theory]
    [InlineData("world’s largest", "world's largest")] // right single quote
    [InlineData("‘quoted’", "'quoted'")] // curly single quotes
    [InlineData("“Big Bang”", "\"Big Bang\"")] // curly double quotes
    [InlineData("2019–2024", "2019-2024")] // en dash
    [InlineData("revenue — up 10%", "revenue - up 10%")] // em dash
    [InlineData("−5%", "-5%")] // minus sign
    [InlineData("non\u00A0breaking", "non breaking")] // no-break space
    public void Fold_TypographicPunctuation_FoldsToAscii(string input, string expected)
    {
        TypographyFold.Fold(input).Should().Be(expected);
    }

    [Fact]
    public void Fold_PlainAsciiText_ReturnsSameInstance()
    {
        const string input = "Total revenue was $100M in fiscal 2026.";

        TypographyFold.Fold(input).Should().BeSameAs(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Fold_NullOrEmpty_ReturnsInput(string input)
    {
        TypographyFold.Fold(input).Should().Be(input);
    }

    [Fact]
    public void Fold_NeverChangesLength()
    {
        // The 1:1 invariant HighlightKeyword depends on: folded indices must address the
        // same characters in the original string.
        const string input = "It’s “official” — 2019–2024 results";

        TypographyFold.Fold(input).Length.Should().Be(input.Length);
    }

    [Fact]
    public void DocumentType_HiddenFromFilingLists_DefaultsToFalse()
    {
        // Registering a type without the flag must change nothing: every built-in filing
        // type stays visible in document lists.
        new DocumentType("SomeType")
            .HiddenFromFilingLists.Should()
            .BeFalse();
        DocumentType.TenK.HiddenFromFilingLists.Should().BeFalse();
        new DocumentType("SomeNews", "Some News", hiddenFromFilingLists: true)
            .HiddenFromFilingLists.Should()
            .BeTrue();
    }
}
