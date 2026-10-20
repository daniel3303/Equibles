using Equibles.Holdings.BusinessLogic.Advisers;
using Equibles.Sec.Data.Models;

namespace Equibles.UnitTests.Holdings.Advisers;

public class ScheduleDFundTypeTests
{
    [Fact]
    public void Of_ListsTypesWithFundsLargestGrossAssetsFirst()
    {
        var filing = new FormAdvPrivateFundFiling
        {
            HedgeFundCount = 2,
            HedgeFundGrossAssets = 100,
            RealEstateFundCount = 1,
            RealEstateFundGrossAssets = 900,
            // A fund without a reported value still lists its type.
            LiquidityFundCount = 1,
        };

        ScheduleDFundType
            .Of(filing)
            .Should()
            .Equal(
                new ScheduleDFundType(FormAdvPrivateFundType.RealEstate, 1, 900),
                new ScheduleDFundType(FormAdvPrivateFundType.Hedge, 2, 100),
                new ScheduleDFundType(FormAdvPrivateFundType.Liquidity, 1, 0)
            );
    }
}
