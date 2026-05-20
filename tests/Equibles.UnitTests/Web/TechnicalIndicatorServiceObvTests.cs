using Equibles.Yahoo.Repositories;

namespace Equibles.UnitTests.Web;

public class TechnicalIndicatorServiceObvTests
{
    [Fact]
    public void ComputeObv_FirstBarIsSeedZero()
    {
        var closes = new List<decimal> { 100m };
        var volumes = new List<long> { 1_000_000 };

        var obv = TechnicalIndicatorService.ComputeObv(closes, volumes);

        obv.Should().ContainSingle().Which.Should().Be(0L);
    }

    [Fact]
    public void ComputeObv_UpDownFlatSequence_AccumulatesAccordingToDirection()
    {
        // i=0: seed 0
        // i=1: close 102 > 100 → +500 → 500
        // i=2: close 101 < 102 → -200 → 300
        // i=3: close 101 == 101 → unchanged → 300
        // i=4: close 105 > 101 → +1000 → 1300
        var closes = new List<decimal> { 100m, 102m, 101m, 101m, 105m };
        var volumes = new List<long> { 999, 500, 200, 700, 1000 };

        var obv = TechnicalIndicatorService.ComputeObv(closes, volumes);

        obv.Should().HaveCount(5);
        obv[0].Should().Be(0L);
        obv[1].Should().Be(500L);
        obv[2].Should().Be(300L);
        obv[3].Should().Be(300L);
        obv[4].Should().Be(1300L);
    }

    [Fact]
    public void ComputeObv_StrictlyRising_AccumulatesAllVolumes()
    {
        // Every bar's close is higher than the previous, so OBV at end equals the sum of
        // volumes from index 1 onward (the seed at index 0 is 0).
        var closes = new List<decimal> { 10, 11, 12, 13, 14 };
        var volumes = new List<long> { 100, 200, 300, 400, 500 };

        var obv = TechnicalIndicatorService.ComputeObv(closes, volumes);

        obv[^1].Should().Be(200 + 300 + 400 + 500);
    }

    [Fact]
    public void ComputeObv_StrictlyFalling_SubtractsAllVolumes()
    {
        var closes = new List<decimal> { 14, 13, 12, 11, 10 };
        var volumes = new List<long> { 100, 200, 300, 400, 500 };

        var obv = TechnicalIndicatorService.ComputeObv(closes, volumes);

        obv[^1].Should().Be(-(200 + 300 + 400 + 500));
    }

    [Fact]
    public void ComputeObv_MismatchedSeriesLengths_Throws()
    {
        var closes = new List<decimal> { 1, 2, 3 };
        var volumes = new List<long> { 100, 200 };

        var act = () => TechnicalIndicatorService.ComputeObv(closes, volumes);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeObv_EmptyInput_ReturnsEmptyList()
    {
        var obv = TechnicalIndicatorService.ComputeObv([], []);

        obv.Should().BeEmpty();
    }

    [Fact]
    public void ComputeObv_ConstantCloses_RemainsAtZero_RegardlessOfVolumes()
    {
        // Every close equals the previous → per contract "leave unchanged on equality",
        // so OBV stays at the seed 0 at every bar. Volumes vary to catch a bug that
        // compares the wrong series, or that treats equality as an up-move (>=).
        var closes = new List<decimal> { 10m, 10m, 10m, 10m };
        var volumes = new List<long> { 100, 250, 500, 750 };

        var obv = TechnicalIndicatorService.ComputeObv(closes, volumes);

        obv.Should().Equal(0L, 0L, 0L, 0L);
    }
}
