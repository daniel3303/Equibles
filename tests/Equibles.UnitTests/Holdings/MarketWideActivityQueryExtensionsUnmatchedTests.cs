using Equibles.Holdings.Repositories.Extensions;
using Equibles.Holdings.Repositories.Models;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Holdings;

// The buy and sell boards rank the change over filers with a 13F in both quarters: a withheld
// prior book (a confidential-treatment quarter) is no sale and a first-time filer's book no buy.
public class MarketWideActivityQueryExtensionsUnmatchedTests
{
    private static readonly Guid Withheld = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid FirstFiler = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Traded = Guid.Parse("00000000-0000-0000-0000-000000000003");

    [Fact]
    public void Boards_RankOnlyTheMatchedChange()
    {
        var rows = Rows();

        rows.TopSellers().Select(r => r.CommonStockId).Should().Equal(Traded);
        rows.AsQueryable().TopSellers().Select(r => r.CommonStockId).Should().Equal(Traded);
        rows.TopBuyers().Should().BeEmpty();
        rows.AsQueryable().TopBuyers().Should().BeEmpty();
        rows.Single(r => r.CommonStockId == Traded)
            .Should()
            .Match<MarketWideStockActivity>(r => r.DeltaShares == -50 && r.DeltaValue == -500);
    }

    private static MarketWideStockActivity[] Rows() =>
        [
            // The whole prior book belongs to a filer without a 13F this quarter.
            new()
            {
                CommonStockId = Withheld,
                PreviousShares = 1_000,
                PreviousValue = 10_000,
                UnmatchedPreviousShares = 1_000,
                UnmatchedPreviousValue = 10_000,
            },
            // The whole current book belongs to a filer with no 13F the quarter before.
            new()
            {
                CommonStockId = FirstFiler,
                CurrentShares = 900,
                CurrentValue = 9_000,
                UnmatchedCurrentShares = 900,
                UnmatchedCurrentValue = 9_000,
            },
            // A matched seller beside a first-time filer that outweighs it.
            new()
            {
                CommonStockId = Traded,
                CurrentShares = 250,
                PreviousShares = 100,
                CurrentValue = 2_500,
                PreviousValue = 1_000,
                UnmatchedCurrentShares = 200,
                UnmatchedCurrentValue = 2_000,
            },
        ];
}
