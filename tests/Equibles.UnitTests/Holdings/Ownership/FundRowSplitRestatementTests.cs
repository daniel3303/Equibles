using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.BusinessLogic.Ownership;

namespace Equibles.UnitTests.Holdings.Ownership;

public class FundRowSplitRestatementTests
{
    private static readonly StockSplit TenForOne = new()
    {
        EffectiveDate = new DateOnly(2024, 6, 10),
        Numerator = 10,
        Denominator = 1,
        PriceSeriesTicker = "NVDA",
        Source = StockSplitSource.Yahoo,
    };

    [Fact]
    public void ABalanceReportedBeforeASplit_IsRestatedOntoTodaysBasis()
    {
        FundContributor
            .RestateBalance(1_234.5m, new DateOnly(2024, 3, 31), [TenForOne])
            .Should()
            .Be(12_345);
    }

    [Fact]
    public void ABalanceReportedAfterTheSplit_StaysAsReported()
    {
        FundContributor
            .RestateBalance(1_234m, new DateOnly(2024, 6, 30), [TenForOne])
            .Should()
            .Be(1_234);
    }

    [Fact]
    public void AFractionalBalance_RoundsHalfAwayFromZero()
    {
        FundContributor.RestateBalance(2.5m, new DateOnly(2024, 6, 30), [TenForOne]).Should().Be(3);
    }
}
