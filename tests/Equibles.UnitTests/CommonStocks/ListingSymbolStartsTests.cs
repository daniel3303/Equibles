using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;

namespace Equibles.UnitTests.CommonStocks;

public class ListingSymbolStartsTests
{
    private static readonly EquityListingReference Fund = new(Guid.NewGuid(), Guid.NewGuid(), "FB");
    private static readonly EquityListingReference Other = new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "AAPL"
    );

    private static readonly ListingSymbolStarts Starts = new([
        new EquityListingSymbolStart
        {
            EquityListingId = Fund.EquityListingId,
            Ticker = "FB",
            StartsOn = new DateOnly(2025, 6, 26),
            EvidenceSource = "test",
        },
    ]);

    private static Dictionary<string, EquityListingReference> Map() =>
        new(StringComparer.Ordinal) { ["FB"] = Fund, ["AAPL"] = Other };

    [Fact]
    public void Admitted_DateBeforeTheStart_DropsOnlyThatListing()
    {
        var admitted = Starts.Admitted(Map(), new DateOnly(2022, 6, 1));

        admitted.Keys.Should().BeEquivalentTo(["AAPL"]);
        admitted.Comparer.Should().BeSameAs(StringComparer.Ordinal);
    }

    [Fact]
    public void Admitted_OnTheStart_KeepsTheSameMap()
    {
        var map = Map();

        Starts.Admitted(map, new DateOnly(2025, 6, 26)).Should().BeSameAs(map);
    }

    [Fact]
    public void Admits_RowsUnderAnotherSymbolOfTheSameListing_AreNotBounded()
    {
        // A renamed line keeps its earlier symbol's rows; a start recorded for its current symbol
        // says nothing about them.
        var renamed = Fund with
        {
            ListedTicker = "META",
        };

        Starts.Admits(renamed, new DateOnly(2022, 6, 1)).Should().BeTrue();
    }

    [Fact]
    public void Admits_SeparatorSpellingOfTheBoundedSymbol_IsBounded()
    {
        var starts = new ListingSymbolStarts([
            new EquityListingSymbolStart
            {
                EquityListingId = Fund.EquityListingId,
                Ticker = "BRK.B",
                StartsOn = new DateOnly(2025, 1, 1),
                EvidenceSource = "test",
            },
        ]);

        starts
            .Admits(Fund with { ListedTicker = "BRK-B" }, new DateOnly(2024, 1, 1))
            .Should()
            .BeFalse();
    }
}
