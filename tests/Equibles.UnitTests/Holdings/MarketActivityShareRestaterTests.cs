using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories.Models;

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

    // A 2-for-1 split between the quarters: the withheld prior book restates with the totals,
    // so it stays out of the delta on today's basis instead of reading as a sale.
    [Fact]
    public void RestateRow_SplitBetweenQuarters_RestatesTheUnmatchedSharesWithTheTotals()
    {
        var row = new MarketWideStockActivity
        {
            ListingShares =
            [
                new()
                {
                    PriceSeriesTicker = "ACME",
                    CurrentShares = 1_200,
                    PreviousShares = 1_000,
                    UnmatchedCurrentShares = 200,
                    UnmatchedPreviousShares = 400,
                },
            ],
        };
        var splits = new List<StockSplit>
        {
            new()
            {
                PriceSeriesTicker = "ACME",
                EffectiveDate = new DateOnly(2025, 2, 15),
                Numerator = 2,
                Denominator = 1,
            },
        };

        MarketActivityShareRestater.RestateRow(
            row,
            new DateOnly(2025, 3, 31),
            new DateOnly(2024, 12, 31),
            "ACME",
            splits
        );

        Assert.Equal(
            (1_200L, 2_000L, 200L, 800L),
            (
                row.CurrentShares,
                row.PreviousShares,
                row.UnmatchedCurrentShares,
                row.UnmatchedPreviousShares
            )
        );
        Assert.Equal(1_000 - 1_200, row.DeltaShares);
    }
}
