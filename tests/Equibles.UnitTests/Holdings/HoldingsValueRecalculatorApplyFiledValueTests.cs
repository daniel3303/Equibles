using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;
using FluentAssertions;

namespace Equibles.UnitTests.Holdings;

public class HoldingsValueRecalculatorApplyFiledValueTests
{
    private static InstitutionalHolding MakeHolding(
        long shares,
        long? filedValue,
        params long[] entryShares
    )
    {
        return new InstitutionalHolding
        {
            Shares = shares,
            FiledValue = filedValue,
            Value = 0L,
            ValuePending = true,
            ManagerEntries = entryShares
                .Select(s => new HoldingManagerEntry { Shares = s, Value = 0L })
                .ToList(),
        };
    }

    [Fact]
    public void ApplyFiledValue_PublishesFiledFigureAndClearsPending()
    {
        var holding = MakeHolding(shares: 1000, filedValue: 868_524L);

        HoldingsValueRecalculator.ApplyFiledValue(holding);

        holding.Value.Should().Be(868_524L);
        holding.ValuePending.Should().BeFalse();
        holding.ValueSource.Should().Be(ValueSource.Filed);
    }

    [Fact]
    public void ApplyFiledValue_SplitsAcrossLegsProportionallyToShares()
    {
        // Legs carry counts, not values, so shares are the only allocation basis the filing
        // supports: 3,000 + 1,000 shares over a $1M filed value → $750k + $250k.
        var holding = MakeHolding(shares: 4000, filedValue: 1_000_000L, 3000, 1000);

        HoldingsValueRecalculator.ApplyFiledValue(holding);

        holding.ManagerEntries[0].Value.Should().Be(750_000L);
        holding.ManagerEntries[1].Value.Should().Be(250_000L);
    }

    [Fact]
    public void ApplyFiledValue_ZeroShares_LeavesLegsAtZeroInsteadOfDividingByZero()
    {
        var holding = MakeHolding(shares: 0, filedValue: 500_000L, 100);

        HoldingsValueRecalculator.ApplyFiledValue(holding);

        holding.Value.Should().Be(500_000L);
        holding.ManagerEntries[0].Value.Should().Be(0L);
    }

    [Fact]
    public void ApplyFiledValue_NullFiledValue_PublishesZero()
    {
        // Callers gate on FiledValue > 0; if one slips through the row must not throw and must
        // not invent a figure.
        var holding = MakeHolding(shares: 1000, filedValue: null, 1000);

        HoldingsValueRecalculator.ApplyFiledValue(holding);

        holding.Value.Should().Be(0L);
        holding.ValueSource.Should().Be(ValueSource.Filed);
        holding.ValuePending.Should().BeFalse();
    }
}
