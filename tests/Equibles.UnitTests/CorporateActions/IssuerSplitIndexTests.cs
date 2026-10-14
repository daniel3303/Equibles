using System.Collections;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;

namespace Equibles.UnitTests.CorporateActions;

public class IssuerSplitIndexTests
{
    private static readonly Guid First = Guid.NewGuid();
    private static readonly Guid Second = Guid.NewGuid();

    [Fact]
    public void For_ManyIssuerReads_EnumerateTheBatchOnce()
    {
        var batch = new CountingSplits(
            Enumerable
                .Range(0, 200)
                .Select(day => Split(day % 2 == 0 ? First : Second, day))
                .ToList()
        );

        var index = new IssuerSplitIndex(batch);
        for (var read = 0; read < 1_000; read++)
        {
            index.For(First).Count().Should().Be(100);
            index.For(Second).Count().Should().Be(100);
        }

        batch.Enumerations.Should().Be(1);
        batch.Yielded.Should().Be(200);
    }

    [Fact]
    public void For_KeepsBatchOrderWithinAnIssuer()
    {
        var late = Split(First, 30);
        var other = Split(Second, 20);
        var early = Split(First, 10);

        var index = new IssuerSplitIndex([late, other, early]);

        index.For(First).Should().Equal(late, early);
        index.For(Second).Should().Equal(other);
    }

    [Fact]
    public void For_IssuerWithoutSplits_IsEmpty()
    {
        new IssuerSplitIndex([Split(First, 1)]).For(Second).Should().BeEmpty();
        new IssuerSplitIndex(null).For(First).Should().BeEmpty();
    }

    private static StockSplit Split(Guid issuerId, int day) =>
        new()
        {
            EquityIssuerId = issuerId,
            EffectiveDate = new DateOnly(2025, 1, 1).AddDays(day),
            Numerator = 2,
            Denominator = 1,
        };

    private sealed class CountingSplits(IReadOnlyList<StockSplit> splits) : IEnumerable<StockSplit>
    {
        public int Enumerations { get; private set; }
        public int Yielded { get; private set; }

        public IEnumerator<StockSplit> GetEnumerator()
        {
            Enumerations++;
            foreach (var split in splits)
            {
                Yielded++;
                yield return split;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
