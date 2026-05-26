using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Sibling-contract pin to ParseBool (GH-2106 / PR #2117). Both `ParseBool`
/// and `ParseTransactionCode` live in `InsiderTradingFilingProcessor`,
/// both consume raw text extracted from SEC Form 4 XML, and both used
/// exact-string matching that silently misclassified whitespace-padded
/// input. ParseBool was fixed to trim. ParseTransactionCode's switch
/// expression still requires an exact single-letter match, so " P " (with
/// any insignificant whitespace SEC filings carry — common in malformed
/// or pretty-printed XML) falls through to `TransactionCode.Other`,
/// silently misclassifying a Purchase as Other and dropping the trade
/// from buy/sell aggregations.
///
/// The single current caller (`ParseTransaction`) pre-trims, masking the
/// defect today. The pin guards the helper's own contract so a future
/// caller reusing it (or a refactor that drops the caller's `.Trim()`)
/// can't silently regress the same way ParseBool did.
/// </summary>
public class InsiderTradingFilingProcessorParseTransactionCodeWhitespacePaddedTests
{
    private static readonly MethodInfo ParseTransactionCodeMethod =
        typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseTransactionCode",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static TransactionCode ParseTransactionCode(string code) =>
        (TransactionCode)ParseTransactionCodeMethod.Invoke(null, [code]);

    [Fact(
        Skip = "GH-2120 — ParseTransactionCode exact-match misclassifies whitespace-padded codes as Other"
    )]
    public void ParseTransactionCode_WhitespacePaddedCode_ResolvesToCorrespondingCode()
    {
        var result = ParseTransactionCode(" P ");

        result.Should().Be(TransactionCode.Purchase);
    }
}
