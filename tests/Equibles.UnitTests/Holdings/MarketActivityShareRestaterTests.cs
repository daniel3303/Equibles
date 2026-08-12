using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings;

public class MarketActivityShareRestaterTests
{
    [Fact]
    public void RestateListingTotal_SiblingOnlySplit_DoesNotMultiplyPrimaryShares()
    {
        var reportDate = new DateOnly(2024, 12, 31);
        var listingShares = new List<StockQuarterlyListingActivity>
        {
            new() { PriceSeriesTicker = "ACME", CurrentShares = 100 },
            new() { PriceSeriesTicker = "ACME.B", CurrentShares = 10 },
        };
        var splits = new List<StockSplit>
        {
            new()
            {
                PriceSeriesTicker = "ACME.B",
                EffectiveDate = new DateOnly(2025, 1, 15),
                Numerator = 10,
                Denominator = 1,
            },
        };

        var result = MarketActivityShareRestater.RestateListingTotal(
            listingShares,
            listing => listing.CurrentShares,
            reportDate,
            "ACME",
            splits
        );

        Assert.Equal(200, result);
    }
}
