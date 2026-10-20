using Equibles.Holdings.BusinessLogic.Intelligence;
using Equibles.Holdings.BusinessLogic.Intelligence.Models;
using Equibles.Holdings.Data.Models;

namespace Equibles.UnitTests.Holdings.Intelligence;

public class HolderOverlapCalculatorTests
{
    private static readonly DateOnly Quarter = new(2026, 6, 30);

    [Fact]
    public void Build_RanksPeersBySharedHoldersAndSkipsThinOverlaps()
    {
        var stock = Key();
        var close = Key();
        var thin = Key();
        var calculator = new HolderOverlapCalculator();
        // Three filers hold the stock and its close peer; only two also hold the thin one.
        calculator.Add([Position(stock), Position(close), Position(thin)]);
        calculator.Add([Position(stock), Position(close), Position(thin)]);
        calculator.Add([Position(stock), Position(close)]);

        var peers = calculator
            .Build(Quarter)
            .Where(row => row.EquityIssuerId == stock.IssuerId)
            .ToList();

        peers.Should().ContainSingle();
        peers[0].PeerEquityIssuerId.Should().Be(close.IssuerId);
        peers[0].Rank.Should().Be(1);
        peers[0].SharedHolders.Should().Be(3);
        peers[0].Similarity.Should().Be(1m);
    }

    [Fact]
    public void Build_NeverPairsASecurityWithItsSiblingClass()
    {
        var classA = Key();
        var classB = classA with { ListedTicker = "CLS.B" };
        var other = Key();
        var calculator = new HolderOverlapCalculator();
        for (var holder = 0; holder < 3; holder++)
            calculator.Add([Position(classA), Position(classB), Position(other)]);

        var peers = calculator
            .Build(Quarter)
            .Where(row => row.EquityIssuerId == classA.IssuerId && row.ListedTicker == null)
            .ToList();

        peers.Should().ContainSingle(row => row.PeerEquityIssuerId == other.IssuerId);
    }

    [Fact]
    public void Build_KeepsTheClosestTwentyPeers()
    {
        var stock = Key();
        var peers = Enumerable.Range(0, 30).Select(_ => Key()).ToList();
        var calculator = new HolderOverlapCalculator();
        for (var holder = 0; holder < 3; holder++)
            calculator.Add([Position(stock), .. peers.Select(peer => Position(peer))]);

        calculator
            .Build(Quarter)
            .Count(row => row.EquityIssuerId == stock.IssuerId)
            .Should()
            .Be(HolderOverlapCalculator.PeersPerSecurity);
    }

    [Fact]
    public void Add_KeepsOnlyTheLargestHundredValuedPositions()
    {
        var stock = Key();
        var small = Key();
        var calculator = new HolderOverlapCalculator();
        for (var holder = 0; holder < 3; holder++)
        {
            var book = Enumerable
                .Range(0, HolderOverlapCalculator.PositionsPerHolder)
                .Select(_ => Position(Key(), 1_000))
                .Append(Position(stock, 1_000))
                .Append(Position(small, 1))
                .ToList();
            calculator.Add(book);
        }

        calculator
            .Build(Quarter)
            .Should()
            .NotContain(row =>
                row.EquityIssuerId == small.IssuerId || row.PeerEquityIssuerId == small.IssuerId
            );
    }

    [Theory]
    [InlineData(HolderStyleBand.Concentrated, true)]
    [InlineData(HolderStyleBand.DiversifiedActive, true)]
    [InlineData(HolderStyleBand.IndexLike, false)]
    [InlineData(HolderStyleBand.SystematicLike, false)]
    public void Counts_OnlyDiscretionaryActiveBooks(HolderStyleBand band, bool counts) =>
        HolderOverlapCalculator.Counts(band).Should().Be(counts);

    private static HolderSecurityKey Key() => new(Guid.NewGuid(), null);

    private static HolderEquityPosition Position(HolderSecurityKey key, long value = 100) =>
        new(key, 10, value);
}
