using Equibles.Cboe.Data.Models;
using Equibles.Cboe.Repositories.Extensions;

namespace Equibles.UnitTests.Cboe;

// A put/call ratio is puts ÷ calls: a genuine row needs a positive call
// denominator and its ratio can never reach total volume (puts/calls ≤ puts <
// total). A historical VIX backfill (all dates on or before 2019-10-04) stored
// the ratio as puts + total — values into the millions — with the call volume
// either missing or set to a 1 sentinel. OnlyReconcilable must drop exactly
// those rows from a served series and keep every genuine row. Issue #3391.
public class CboePutCallRatioReconciliationTests
{
    private static CboePutCallRatio Row(
        CboePutCallRatioType type,
        DateOnly date,
        long? callVolume,
        long? putVolume,
        long? totalVolume,
        decimal? putCallRatio
    ) =>
        new()
        {
            RatioType = type,
            Date = date,
            CallVolume = callVolume,
            PutVolume = putVolume,
            TotalVolume = totalVolume,
            PutCallRatio = putCallRatio,
        };

    [Fact]
    public void OnlyReconcilable_DropsCorruptVixRows_KeepsGenuineRows()
    {
        var rows = new[]
        {
            // Corrupt: call volume missing, ratio stored as puts + total.
            Row(
                CboePutCallRatioType.Vix,
                new DateOnly(2018, 5, 1),
                null,
                200_000,
                300_000,
                500_000m
            ),
            // Corrupt: call volume is a 1 sentinel, ratio = puts + total (117973).
            Row(CboePutCallRatioType.Vix, new DateOnly(2011, 11, 25), 1, 59_059, 58_914, 117_973m),
            // Genuine VIX row (0.67 ≈ 350284 / 523819).
            Row(
                CboePutCallRatioType.Vix,
                new DateOnly(2025, 6, 12),
                523_819,
                350_284,
                874_103,
                0.67m
            ),
            // Genuine Total row, unaffected by the VIX corruption.
            Row(
                CboePutCallRatioType.Total,
                new DateOnly(2025, 6, 12),
                2_000_000,
                1_500_000,
                3_500_000,
                0.75m
            ),
        };

        var kept = rows.AsQueryable().OnlyReconcilable().ToList();

        kept.Should().HaveCount(2);
        kept.Should().NotContain(r => r.CallVolume == null);
        kept.Should().NotContain(r => r.PutCallRatio >= r.TotalVolume);
        kept.Select(r => r.RatioType)
            .Should()
            .BeEquivalentTo(new[] { CboePutCallRatioType.Vix, CboePutCallRatioType.Total });
    }

    [Fact]
    public void OnlyReconcilable_KeepsZeroRatioRow_WhenCallVolumePresent()
    {
        // A quiet day with no put volume is a genuine 0.0 ratio, not corruption.
        var rows = new[]
        {
            Row(CboePutCallRatioType.Vix, new DateOnly(2025, 6, 12), 100_000, 0, 100_000, 0m),
        };

        var kept = rows.AsQueryable().OnlyReconcilable().ToList();

        kept.Should().ContainSingle();
    }
}
