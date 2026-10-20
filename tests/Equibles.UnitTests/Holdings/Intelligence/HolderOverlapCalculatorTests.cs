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
    public void Build_CountsAnIssuersShareClassesAsOneCompany()
    {
        // Alphabet-like: one filer holds both classes, two hold only one each.
        var classA = Key();
        var classC = classA with { ListedTicker = "CLS.C" };
        var other = Key();
        var calculator = new HolderOverlapCalculator();
        calculator.Add([Position(classA), Position(classC), Position(other)]);
        calculator.Add([Position(classA), Position(other)]);
        calculator.Add([Position(classC), Position(other)]);

        var rows = calculator.Build(Quarter);

        var peers = rows.Where(row => row.EquityIssuerId == other.IssuerId).ToList();
        peers.Should().ContainSingle();
        peers[0].PeerEquityIssuerId.Should().Be(classA.IssuerId);
        peers[0].PeerListedTicker.Should().BeNull();
        peers[0].SharedHolders.Should().Be(3, "each filer counts the company once");
        rows.Should().NotContain(row => row.ListedTicker != null || row.PeerListedTicker != null);
        rows.Should()
            .NotContain(row =>
                row.EquityIssuerId == classA.IssuerId && row.PeerEquityIssuerId == classA.IssuerId
            );
    }

    [Fact]
    public void Build_LeavesFundsOutOfThePeers()
    {
        // An index ETF, a registered closed-end fund and an operating company's exchange-traded
        // note, all held beside two operating companies.
        var stock = Key();
        var peer = Key();
        var etf = Key();
        var closedEnd = Key();
        var bank = Key();
        var note = bank with { ListedTicker = "NOTE" };
        var funds = new FundSecurities([note], [etf.IssuerId, closedEnd.IssuerId]);
        var calculator = new HolderOverlapCalculator(funds);
        for (var holder = 0; holder < 3; holder++)
            calculator.Add(
                [
                    Position(stock),
                    Position(peer),
                    Position(etf, 10_000),
                    Position(closedEnd, 10_000),
                    Position(note, 10_000),
                ]
            );
        calculator.Add([Position(stock), Position(bank)]);
        calculator.Add([Position(stock), Position(bank)]);
        calculator.Add([Position(stock), Position(bank)]);

        var rows = calculator.Build(Quarter);

        rows.Select(row => row.EquityIssuerId)
            .Concat(rows.Select(row => row.PeerEquityIssuerId))
            .Should()
            .NotContain([etf.IssuerId, closedEnd.IssuerId]);
        rows.Where(row => row.EquityIssuerId == stock.IssuerId)
            .Select(row => (row.PeerEquityIssuerId, row.SharedHolders))
            .Should()
            .BeEquivalentTo([(peer.IssuerId, 3), (bank.IssuerId, 3)], "the bank's note is not the bank");
    }

    [Fact]
    public void Add_FillsTheHundredWithOperatingCompaniesWhenFundsAreLarger()
    {
        var stock = Key();
        var fund = Key();
        var calculator = new HolderOverlapCalculator(new FundSecurities([], [fund.IssuerId]));
        var others = Enumerable
            .Range(0, HolderOverlapCalculator.PositionsPerHolder - 1)
            .Select(_ => Key())
            .ToList();
        for (var holder = 0; holder < 3; holder++)
            calculator.Add(
                [
                    Position(fund, 1_000_000),
                    .. others.Select(other => Position(other, 1_000)),
                    Position(stock, 1),
                ]
            );

        calculator
            .Build(Quarter)
            .Should()
            .Contain(row => row.EquityIssuerId == stock.IssuerId, "the fund frees its slot");
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
