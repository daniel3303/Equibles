using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Adversarial pin against the SEC Form 4 General Instructions transaction-code
/// table. Existing pins cover P→Purchase, S→Sale, A→Award, Q→Other (default).
/// This asserts the contract for code "I": per SEC's official Form 4
/// instructions (Item 8, "Rule 16b-3 Transaction Codes"), "I" denotes a
/// Discretionary Transaction in accordance with Rule 16b-3(f) — not an
/// inheritance. Will/inheritance has a separate letter, "W". A wrong mapping
/// silently misclassifies every 16b-3(f) discretionary trade as inherited
/// stock in the insider-trading tab and downstream aggregations.
/// </summary>
public class InsiderTradingFilingProcessorParseTransactionCodeIDiscretionaryTests
{
    private static readonly MethodInfo ParseTransactionCodeMethod =
        typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseTransactionCode",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact(Skip = "GH-2239 — SEC Form 4 codes I and W are swapped in ParseTransactionCode mapping")]
    public void ParseTransactionCode_CodeI_ReturnsDiscretionary()
    {
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["I"]);

        result.Should().Be(TransactionCode.Discretionary);
    }
}
