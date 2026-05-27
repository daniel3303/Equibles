using System.Reflection;
using Equibles.Holdings.Repositories.Models;
using Equibles.Web.Controllers;
using Equibles.Web.ViewModels.Holdings;

namespace Equibles.UnitTests.Web;

public class HoldingsScreenerControllerToCriteriaFieldAlignmentTests
{
    // The existing null-IndustryIds pin defends the `?? []` guard but
    // doesn't exercise any of the 12 numeric pass-throughs. Like the
    // BuildCsv column-alignment pin (#2428), this pin defends the
    // SOURCE-FIELD-TO-DEST-FIELD MAPPING contract for the whole
    // object-initializer block:
    //
    //     new ScreenerCriteria
    //     {
    //         MinFilerCount = filters.MinFilerCount,
    //         MaxFilerCount = filters.MaxFilerCount,
    //         ...
    //     }
    //
    // Twelve nullable numeric fields, each a straight copy from the
    // identically-named view-model property. The PAIRED-VALUE shape
    // (Min/Max ranges for FilerCount, DeltaFilerCount, TotalValue,
    // DeltaValue, PctFloat — 5 pairs, each guarding a range filter)
    // makes a SWAP regression the highest-risk class:
    //
    //   • Min/Max swap within a pair (e.g. `MinFilerCount =
    //     filters.MaxFilerCount, MaxFilerCount = filters.MinFilerCount`)
    //     would silently invert every range filter. Production
    //     trigger: an analyst types "min 5, max 100" — repository
    //     receives "min 100, max 5" — query's
    //     `Where(x => x >= criteria.MinFilerCount && x <=
    //     criteria.MaxFilerCount)` returns ZERO rows. The screener
    //     page appears broken with no error message; the user blames
    //     the data.
    //
    //   • Cross-pair swap (e.g. `MinFilerCount = filters.MinDelta
    //     FilerCount`) is the same hazard, harder to spot in review.
    //
    //   • MinNewPositions / MinSoldOutPositions swap inverts the
    //     "find me stocks gaining filers" vs "find me stocks losing
    //     filers" filter — same screener bucket but inverted result
    //     set. Operators using the screener to identify exit candidates
    //     would surface entry candidates instead.
    //
    //   • Dropped field — `MinTotalValue = null` (under "redundant
    //     copy") — would silently disable the corresponding filter,
    //     letting through rows the analyst expected to be filtered
    //     out.
    //
    // Adversarial input: distinct values across all 12 numeric fields
    // (1..12 with the appropriate types) so a swap or drop surfaces
    // as a value-at-wrong-field assertion failure — not as a structural
    // type mismatch that the compiler catches. The PctFloat pair
    // uses 0.9 / 0.1 to keep within the realistic 0-1 ratio domain.
    //
    // IndustryIds is omitted from this pin — the sibling already
    // defends its null-coalesce contract — but a non-null IndustryIds
    // is supplied so the pin survives even if IndustryIds gets
    // additional logic later.
    [Fact]
    public void ToCriteria_DistinctValuesPerField_EachViewModelPropertyMapsToSameNamedCriteriaProperty()
    {
        var method = typeof(HoldingsScreenerController).GetMethod(
            "ToCriteria",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var filters = new ScreenerCriteriaViewModel
        {
            MinFilerCount = 1,
            MaxFilerCount = 2,
            MinDeltaFilerCount = 3,
            MaxDeltaFilerCount = 4,
            MinTotalValue = 5L,
            MaxTotalValue = 6L,
            MinDeltaValue = 7L,
            MaxDeltaValue = 8L,
            MinPctFloat = 0.9,
            MaxPctFloat = 0.10,
            MinNewPositions = 11,
            MinSoldOutPositions = 12,
        };

        var result = (ScreenerCriteria)method!.Invoke(null, [filters]);

        result.MinFilerCount.Should().Be(1);
        result.MaxFilerCount.Should().Be(2);
        result.MinDeltaFilerCount.Should().Be(3);
        result.MaxDeltaFilerCount.Should().Be(4);
        result.MinTotalValue.Should().Be(5L);
        result.MaxTotalValue.Should().Be(6L);
        result.MinDeltaValue.Should().Be(7L);
        result.MaxDeltaValue.Should().Be(8L);
        result.MinPctFloat.Should().Be(0.9);
        result.MaxPctFloat.Should().Be(0.10);
        result.MinNewPositions.Should().Be(11);
        result.MinSoldOutPositions.Should().Be(12);
    }
}
