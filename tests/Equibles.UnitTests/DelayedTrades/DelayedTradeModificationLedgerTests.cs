using Equibles.DelayedTrades.BusinessLogic.Prints;
using Equibles.Integrations.DelayedTrades;

namespace Equibles.UnitTests.DelayedTrades;

/// <summary>
/// Contract: a cancelled identifier drops every row carrying it, an amended identifier keeps only the
/// latest amendment row, the cancellation row itself never counts, and an unmodified row passes.
/// </summary>
public class DelayedTradeModificationLedgerTests
{
    private static DelayedTradePrint Print(
        string id,
        DelayedTradeModification modification,
        int line
    ) => DelayedTradePrintFilterTests.Print(tradeId: id, modification: modification, line: line);

    [Fact]
    public void Cancellation_DropsTheOriginalAndTheCancellationRow()
    {
        var original = Print("C1", DelayedTradeModification.None, 3);
        var cancel = Print("C1", DelayedTradeModification.Cancellation, 9);
        var ledger = DelayedTradeModificationLedger.Build([original, cancel]);

        ledger.CancelledCount.Should().Be(1);
        ledger.IsEffective(original).Should().BeFalse();
        ledger.IsEffective(cancel).Should().BeFalse();
    }

    [Fact]
    public void Amendment_KeepsOnlyTheLatestAmendmentRow()
    {
        var original = Print("A1", DelayedTradeModification.None, 3);
        var first = Print("A1", DelayedTradeModification.Amendment, 5);
        var latest = Print("A1", DelayedTradeModification.Amendment, 8);
        var ledger = DelayedTradeModificationLedger.Build([original, first, latest]);

        ledger.AmendedCount.Should().Be(1);
        ledger.IsEffective(original).Should().BeFalse();
        ledger.IsEffective(first).Should().BeFalse();
        ledger.IsEffective(latest).Should().BeTrue();
    }

    [Fact]
    public void AnUnmodifiedRow_IsEffective_EvenWithoutAnIdentifier()
    {
        var ledger = DelayedTradeModificationLedger.Build([]);
        ledger.IsEffective(Print("T1", DelayedTradeModification.None, 3)).Should().BeTrue();
        ledger.IsEffective(Print("", DelayedTradeModification.None, 4)).Should().BeTrue();
        ledger
            .IsEffective(Print("", DelayedTradeModification.Amendment, 5))
            .Should()
            .BeFalse("an amendment without an identifier amends nothing");
    }
}
