using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

public class CongressToolsApplyTransactionTypeFilterCaseInsensitiveTests
{
    // ApplyTransactionTypeFilter is unpinned. Its body:
    //     Enum.TryParse<CongressTransactionType>(transactionType, true, out var parsedType)
    // — the `true` argument enables CASE-INSENSITIVE parsing. The
    // GetMemberTrades MCP tool routes its `transactionType` parameter
    // through this helper, and LLM clients produce user-natural casings
    // ("purchase", "Sale", "SALE") far more often than the canonical
    // "Purchase"/"Sale" form.
    //
    // The risks this pin uniquely catches:
    //
    //   • Drop-the-true regression — `Enum.TryParse<CongressTransactionType>
    //     (transactionType, out var parsedType)` (someone "simplifies"
    //     the overload set) would compile, default to case-SENSITIVE
    //     parsing, and silently REJECT every lowercase user input —
    //     falling through to "return query unchanged" and rendering
    //     UNFILTERED results when the user asked for "purchase only".
    //     The MCP response would include sales too, confusing the LLM's
    //     downstream analysis.
    //
    //   • Drop-the-filter regression — `return query;` instead of
    //     `return query.Where(...)` (the happy-path filter arm) —
    //     would return all rows for every recognised type. Caught
    //     by this pin's "filter applied" count assertion.
    //
    //   • Wrong-enum-value regression — parsing succeeds but matches
    //     the wrong variant (e.g. "purchase" → Sale). Caught by
    //     this pin's distinct-value assertion (the surviving trade
    //     must be the Purchase one).
    //
    // Adversarial input: lowercase "purchase" against a two-trade
    // IQueryable (one Purchase, one Sale). Working code: returns 1
    // trade (the Purchase). Drop-the-true: returns 2 trades (filter
    // skipped because parse failed). Wrong-enum: returns 1 trade
    // but it's the Sale.
    [Fact]
    public void ApplyTransactionTypeFilter_LowercasePurchase_FiltersToPurchaseTradesViaCaseInsensitiveParse()
    {
        var method = typeof(CongressTools).GetMethod(
            "ApplyTransactionTypeFilter",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var purchaseId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var trades = new List<CongressionalTrade>
        {
            new() { Id = purchaseId, TransactionType = CongressTransactionType.Purchase },
            new() { Id = saleId, TransactionType = CongressTransactionType.Sale },
        }.AsQueryable();

        var filtered = (IQueryable<CongressionalTrade>)method!.Invoke(null, [trades, "purchase"]);

        var results = filtered.ToList();
        results.Should().ContainSingle();
        results[0].TransactionType.Should().Be(CongressTransactionType.Purchase);
        results[0].Id.Should().Be(purchaseId);
    }
}
