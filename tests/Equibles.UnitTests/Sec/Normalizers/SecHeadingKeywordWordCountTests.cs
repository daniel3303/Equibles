using Equibles.Sec.BusinessLogic.Normalizers;

namespace Equibles.UnitTests.Sec.Normalizers;

public class SecHeadingKeywordWordCountTests
{
    // Contract (from the doc-comment): WordCount is a whitespace-delimited word count in which
    // EDGAR's non-breaking space (U+00A0) "counts as a separator like any other Unicode
    // whitespace". A header whose keyword and identifier are joined only by an nbsp must still
    // count as two words — pins that the split sees all Unicode whitespace, not just ASCII
    // spaces, so a refactor to Split(' ') (which would undercount this to one) is caught.
    [Fact]
    public void WordCount_NonBreakingSpaceSeparator_CountsTokensOnBothSides()
    {
        var count = SecHeadingKeyword.WordCount("Item\u00A01A");

        count.Should().Be(2, "U+00A0 is Unicode whitespace and must separate 'Item' from '1A'");
    }
}
