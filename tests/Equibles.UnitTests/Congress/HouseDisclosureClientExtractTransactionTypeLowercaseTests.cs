using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Sister to <see cref="HouseDisclosureClientTests.ExtractTransactionType_LowercaseSaleS_StillReturnsSaleViaIgnoreCase"/>
/// — that test pins the case-insensitive behaviour of SaleTypeRegex as a
/// feature ("StillReturnsSale**ViaIgnoreCase**"). PurchaseTypeRegex omits the
/// matching `RegexOptions.IgnoreCase` flag, so by the precedent the Sale test
/// established, a lowercase 'p' transaction marker is currently misclassified
/// as "no transaction type" instead of Purchase. Contract derived from the
/// existing Sale test's documented expectation before reading PurchaseTypeRegex.
/// </summary>
public class HouseDisclosureClientExtractTransactionTypeLowercaseTests
{
    [Fact(
        Skip = "GH-993 — PurchaseTypeRegex omits RegexOptions.IgnoreCase, asymmetric to SaleTypeRegex"
    )]
    public void ExtractTransactionType_LowercasePurchaseP_StillReturnsPurchaseViaIgnoreCase()
    {
        var method = typeof(HouseDisclosureClient).GetMethod(
            "ExtractTransactionType",
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

        var result = (CongressTransactionType?)method.Invoke(null, ["AAPL p"]);

        result
            .Should()
            .Be(
                CongressTransactionType.Purchase,
                "the Sale-side test pins lowercase 's' as a feature; the same case-insensitive treatment should apply to Purchase"
            );
    }
}
