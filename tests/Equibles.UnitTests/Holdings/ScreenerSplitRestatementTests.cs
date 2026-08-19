using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// The in-memory half of the split-aware screener: a stock that split after the screened
/// quarter gets its CurrentShares and % of float restated onto today's basis from exact
/// per-listing sums, and the % filters re-judge the restated ratio (the SQL predicates
/// passed such stocks through un-judged).
/// </summary>
public class ScreenerSplitRestatementTests
{
    private static readonly DateOnly Current = new(2026, 6, 30);

    [Fact]
    public void RestateRow_ReverseSplitAfterTheQuarter_ShrinksSharesAndPercent()
    {
        // The BYND shape: 1-for-30 reverse split after the screened quarter. The as-filed
        // 30M count over the post-split 2M float would read 1500%; restated it is 50%.
        var row = new ScreenerRow
        {
            Ticker = "BYND",
            SharesOutStanding = 2_000_000,
            CurrentShares = 30_000_000,
            PercentOfFloat = 1_500.0,
        };
        List<StockSplit> splits =
        [
            new StockSplit
            {
                EffectiveDate = Current.AddDays(20),
                Numerator = 1m,
                Denominator = 30m,
                PriceSeriesTicker = "BYND",
            },
        ];

        ScreenerSplitRestatement.RestateRow(
            row,
            [new ScreenerListingShares { ListedTicker = null, Shares = 30_000_000 }],
            splits,
            Current
        );

        row.CurrentShares.Should().Be(1_000_000);
        row.PercentOfFloat.Should().Be(50.0);
    }

    [Fact]
    public void RestateRow_SiblingListingSlice_IsNeverRescaledByThePrimarySplit()
    {
        var row = new ScreenerRow
        {
            Ticker = "GOOGL",
            SharesOutStanding = 100_000,
            CurrentShares = 1_700,
        };
        List<StockSplit> splits =
        [
            new StockSplit
            {
                EffectiveDate = Current.AddDays(5),
                Numerator = 10m,
                Denominator = 1m,
                PriceSeriesTicker = "GOOGL",
            },
        ];

        ScreenerSplitRestatement.RestateRow(
            row,
            [
                new ScreenerListingShares { ListedTicker = null, Shares = 1_000 },
                new ScreenerListingShares { ListedTicker = "GOOG", Shares = 700 },
            ],
            splits,
            Current
        );

        // Primary 1,000 → 10,000; the sibling class's 700 stays as filed.
        row.CurrentShares.Should().Be(10_700);
        row.PercentOfFloat.Should().Be(10.7);
    }

    [Fact]
    public void RestateRow_UnknownSharesOutstanding_AnswersNullPercent()
    {
        var row = new ScreenerRow
        {
            Ticker = "TINY",
            SharesOutStanding = 0,
            PercentOfFloat = null,
        };

        ScreenerSplitRestatement.RestateRow(
            row,
            [new ScreenerListingShares { ListedTicker = null, Shares = 5_000 }],
            [
                new StockSplit
                {
                    EffectiveDate = Current.AddDays(1),
                    Numerator = 2m,
                    Denominator = 1m,
                    PriceSeriesTicker = "TINY",
                },
            ],
            Current
        );

        row.CurrentShares.Should().Be(10_000);
        row.PercentOfFloat.Should().BeNull();
    }

    [Fact]
    public void PassesPctFloat_MirrorsTheSqlPredicates()
    {
        var criteria = new ScreenerCriteria { MinPctFloat = 10.0, MaxPctFloat = 60.0 };

        ScreenerSplitRestatement
            .PassesPctFloat(new ScreenerRow { PercentOfFloat = 25.0 }, criteria)
            .Should()
            .BeTrue();
        ScreenerSplitRestatement
            .PassesPctFloat(new ScreenerRow { PercentOfFloat = 5.0 }, criteria)
            .Should()
            .BeFalse();
        ScreenerSplitRestatement
            .PassesPctFloat(new ScreenerRow { PercentOfFloat = 75.0 }, criteria)
            .Should()
            .BeFalse();
        // Unknown SharesOutStanding (null %) fails any active bound, matching SQL.
        ScreenerSplitRestatement
            .PassesPctFloat(new ScreenerRow { PercentOfFloat = null }, criteria)
            .Should()
            .BeFalse();
        // No bounds set: everything passes.
        ScreenerSplitRestatement
            .PassesPctFloat(new ScreenerRow { PercentOfFloat = null }, new ScreenerCriteria())
            .Should()
            .BeTrue();
    }
}
