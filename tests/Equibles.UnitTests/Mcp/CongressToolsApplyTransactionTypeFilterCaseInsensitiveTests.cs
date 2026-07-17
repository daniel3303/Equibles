using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;

namespace Equibles.UnitTests.Mcp;

/// <summary>
/// Pins <c>ParseTransactionTypeArgument</c>, the strict transaction-type parser shared by
/// GetCongressionalTrades and GetMemberTrades. The contract: case-insensitive Purchase/Sale
/// plus the Buy/Sell synonyms an LLM naturally reaches for; anything else — including numeric
/// enum strings, which <c>Enum.TryParse</c> would happily accept as undefined values — must
/// produce an explicit error naming the accepted values, never a silently unfiltered result
/// (the previous behavior returned ALL trades for "Buy", which callers misread as purchases).
/// </summary>
public class CongressToolsApplyTransactionTypeFilterCaseInsensitiveTests
{
    private static (CongressTransactionType? Type, string Error) Parse(string transactionType)
    {
        var method = typeof(CongressTools).GetMethod(
            "ParseTransactionTypeArgument",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        return ((CongressTransactionType? Type, string Error))
            method!.Invoke(null, [transactionType])!;
    }

    [Theory]
    [InlineData("Purchase", CongressTransactionType.Purchase)]
    [InlineData("purchase", CongressTransactionType.Purchase)]
    [InlineData("PURCHASE", CongressTransactionType.Purchase)]
    [InlineData("Buy", CongressTransactionType.Purchase)]
    [InlineData("buy", CongressTransactionType.Purchase)]
    [InlineData("Sale", CongressTransactionType.Sale)]
    [InlineData("sale", CongressTransactionType.Sale)]
    [InlineData("Sell", CongressTransactionType.Sale)]
    [InlineData("SELL", CongressTransactionType.Sale)]
    [InlineData(" Sale ", CongressTransactionType.Sale)]
    public void ParseTransactionTypeArgument_KnownValueAnyCase_ParsesWithoutError(
        string input,
        CongressTransactionType expected
    )
    {
        var (type, error) = Parse(input);

        error.Should().BeNull();
        type.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseTransactionTypeArgument_Blank_MeansNoFilter(string input)
    {
        var (type, error) = Parse(input);

        error.Should().BeNull();
        type.Should().BeNull();
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("Exchange")]
    // Enum.TryParse would accept a numeric string as an undefined enum value that filters
    // every row out; the strict parser must reject it like any other unknown value.
    [InlineData("2")]
    [InlineData("0")]
    public void ParseTransactionTypeArgument_UnknownValue_ErrorsListingAcceptedValues(string input)
    {
        var (type, error) = Parse(input);

        type.Should().BeNull();
        error
            .Should()
            .Be(
                $"Unknown transactionType '{input}'. Accepted: Purchase or Sale (synonyms: Buy, Sell)."
            );
    }
}
