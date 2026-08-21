using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.Data;

namespace Equibles.UnitTests.Sec;

public class FinancialFactSourcePriorityTests
{
    [Theory]
    [InlineData("TwentyFa", 0)]
    [InlineData("FortyFa", 0)]
    [InlineData("SixKa", 1)]
    public void Rank_ForeignAmendment_MatchesItsBaseForm(string formValue, int expectedRank)
    {
        FinancialFactSourcePriority
            .Rank(DocumentType.FromValue(formValue))
            .Should()
            .Be(expectedRank);
    }
}
