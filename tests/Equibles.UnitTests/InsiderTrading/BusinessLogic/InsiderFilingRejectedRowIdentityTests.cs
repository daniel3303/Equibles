using System.Xml.Linq;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

public class InsiderFilingRejectedRowIdentityTests
{
    [Theory]
    [InlineData("snex", "2024-02-14", 6, new int[] { 1, 2, 3, 5 })]
    [InlineData("spgi", "2024-02-05", 5, new int[] { 0 })]
    [InlineData("oprx", "2024-02-21", 2, new int[] { })]
    [InlineData("gic", "2024-01-09", 2, new int[] { 0 })]
    [InlineData("gwrs", "2024-05-23", 4, new int[] { 0, 1 })]
    public void RealSource_InvalidDatesCannotShiftLaterTrades(
        string name,
        string filed,
        int sourceCount,
        int[] visibleOrders
    )
    {
        var root = XElement.Load(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "InsiderTrading",
                "InvalidDates",
                name + ".xml"
            )
        );
        var filing = new FilingData
        {
            FilingDate = DateOnly.Parse(filed),
            ReportDate = InsiderFilingParser.ParsePeriodOfReport(root).Value,
        };
        var owner = new InsiderOwner();
        var stock = Guid.NewGuid();
        var visible = InsiderFilingParser.ParseTransactions(root, owner, stock, filing, false);
        var replay = InsiderFilingParser.ParseTransactionsForReplay(
            root,
            owner,
            stock,
            filing,
            false
        );
        Assert.Equal(visibleOrders, visible.Select(t => t.TransactionOrder));
        Assert.Equal(sourceCount, replay.Count);
        Assert.All(
            replay.Where(t => !visibleOrders.Contains(t.TransactionOrder)),
            t => Assert.True(t.TransactionDate.Year < 1900)
        );
        var stored = replay
            .Select(t => new InsiderTransaction
            {
                TransactionOrder = t.TransactionOrder,
                Shares = t.Shares,
                ReportedPricePerShare = t.PricePerShare,
                SecurityTitle = t.SecurityTitle,
                TransactionDate = new(2024, 2, 13),
                TransactionCode = TransactionCode.Sale,
                SecurityKind = InsiderSecurityKind.Unknown,
            })
            .ToList();
        var matches = InsiderFilingRowMatches.Match(stored, replay);
        Assert.Equal(sourceCount, matches.Count);
        Assert.All(stored, t => Assert.Equal(t.TransactionOrder, matches[t.Id].TransactionOrder));
    }

    [Fact]
    public void CompactedLegacyRows_MatchOnlyUniqueUnchangedEvidence()
    {
        var stored = new InsiderTransaction
        {
            TransactionOrder = 0,
            SecurityTitle = "Common Stock",
            Shares = 20,
            ReportedPricePerShare = 5,
        };
        var rejected = new InsiderTransaction
        {
            TransactionOrder = 0,
            SecurityTitle = "Common Stock",
            Shares = 10,
            PricePerShare = 5,
        };
        var valid = new InsiderTransaction
        {
            TransactionOrder = 1,
            SecurityTitle = "Common Stock",
            Shares = 20,
            PricePerShare = 5,
        };
        Assert.Same(valid, InsiderFilingRowMatches.Match([stored], [rejected, valid])[stored.Id]);
        rejected.Shares = 20;
        Assert.Empty(InsiderFilingRowMatches.Match([stored], [rejected, valid]));
    }
}
