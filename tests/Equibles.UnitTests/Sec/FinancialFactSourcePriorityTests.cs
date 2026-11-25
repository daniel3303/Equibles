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

    // A year is the annual report's to state: a quarterly report repeating it ranks after it, while
    // a quarterly report's own quarter keeps the periodic rank.
    [Theory]
    [InlineData("TenK", 364, 0)]
    [InlineData("TenQ", 364, 1)]
    [InlineData("TenQ", 90, 0)]
    [InlineData("EightK", 364, 1)]
    public void Rank_ForAPeriod_RanksAQuarterlyReportsYearAfterTheAnnualReport(
        string formValue,
        int spanDays,
        int expectedRank
    )
    {
        var start = new DateOnly(2025, 1, 1);

        FinancialFactSourcePriority
            .Rank(DocumentType.FromValue(formValue), start, start.AddDays(spanDays))
            .Should()
            .Be(expectedRank);
    }

    // A figure read from a statement table fills gaps, so any tagged form for the same cell wins.
    [Fact]
    public void Rank_StatementTableForm_IsBelowTheTaggedSixK()
    {
        FinancialFactSourcePriority
            .Rank(StatementTableForms.For(DocumentType.SixK))
            .Should()
            .BeGreaterThan(FinancialFactSourcePriority.Rank(DocumentType.SixK));
    }
}
