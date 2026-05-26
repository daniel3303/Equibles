using System.Reflection;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Symmetric half of the I/W swap pinned in GH-2239
/// (InsiderTradingFilingProcessorParseTransactionCodeIDiscretionaryTests). Per
/// the SEC Form 4 General Instructions Item 8 transaction-code table, "W"
/// denotes an Acquisition or disposition by will or the laws of descent and
/// distribution — i.e. an inheritance — *not* a discretionary trade. Pinning
/// only the I→Discretionary direction would let a half-fix (e.g. swapping I's
/// arm in isolation) pass while still misclassifying every actual will/descent
/// transfer. This is the second regression net the fix in GH-2239 must clear.
/// </summary>
public class InsiderTradingFilingProcessorParseTransactionCodeWInheritanceTests
{
    private static readonly MethodInfo ParseTransactionCodeMethod =
        typeof(InsiderTradingFilingProcessor).GetMethod(
            "ParseTransactionCode",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    [Fact(Skip = "GH-2239 — SEC Form 4 codes I and W are swapped in ParseTransactionCode mapping")]
    public void ParseTransactionCode_CodeW_ReturnsInheritance()
    {
        var result = (TransactionCode)ParseTransactionCodeMethod.Invoke(null, ["W"]);

        result.Should().Be(TransactionCode.Inheritance);
    }
}
