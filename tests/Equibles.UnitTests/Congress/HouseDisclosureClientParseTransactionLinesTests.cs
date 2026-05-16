using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Pins <c>HouseDisclosureClient.ParseTransactionLines</c> — the PTR text
/// parser (owner-code/date/amount regexes, purchase vs sale type, ticker
/// extraction). It's a pure private method; invoked directly via reflection
/// with crafted House-PTR-shaped lines plus the skip paths (no owner code, no
/// date, no transaction type) so a malformed line is dropped, not fatal.
/// </summary>
public class HouseDisclosureClientParseTransactionLinesTests
{
    [Fact]
    public void ParseTransactionLines_MixedValidAndMalformedLines_ParsesOnlyTheValidOnes()
    {
        var client = new HouseDisclosureClient(
            new HttpClient(),
            Substitute.For<ILogger<HouseDisclosureClient>>()
        );

        var filingType = typeof(HouseDisclosureClient).GetNestedType(
            "HouseFiling",
            BindingFlags.NonPublic
        );
        var filing = Activator.CreateInstance(
            filingType,
            "Jane Smith",
            "20012345",
            new DateOnly(2025, 1, 20),
            "CA01"
        );

        var lines = new[]
        {
            "Periodic Transaction Report header — no owner code, skipped",
            "SP Apple Inc. (AAPL) P 01/14/2025 01/20/2025 $1,001 - $15,000",
            "JT Tesla Inc (TSLA) S (partial) 12/31/2024 01/05/2025 $15,001 - $50,000",
            "DC No date asset here — owner but no date, skipped",
            "Self Microsoft Corp (MSFT) 03/03/2023 03/10/2023 $1,001 - $15,000",
        };

        var method = typeof(HouseDisclosureClient).GetMethod(
            "ParseTransactionLines",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var result = (List<DisclosureTransaction>)method.Invoke(client, [lines, filing]);

        result.Should().HaveCount(2, "only the purchase and sale lines are well-formed");

        var purchase = result.Single(t => t.TransactionType == CongressTransactionType.Purchase);
        purchase.Ticker.Should().Be("AAPL");
        purchase.MemberName.Should().Be("Jane Smith");
        purchase.OwnerType.Should().Be("SP");
        purchase.TransactionDate.Should().Be(new DateOnly(2025, 1, 14));
        purchase.AmountFrom.Should().Be(1001);
        purchase.AmountTo.Should().Be(15000);

        var sale = result.Single(t => t.TransactionType == CongressTransactionType.Sale);
        sale.Ticker.Should().Be("TSLA");
        sale.OwnerType.Should().Be("JT");
        sale.TransactionDate.Should().Be(new DateOnly(2024, 12, 31));
    }
}
