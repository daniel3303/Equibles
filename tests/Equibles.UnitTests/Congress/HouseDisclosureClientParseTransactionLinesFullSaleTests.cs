using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Adversarial sibling to <see cref="HouseDisclosureClientParseTransactionLinesTests"/>,
/// which exercises only the <c>S (partial)</c> sale variant. The House PTR
/// contract documented on ExtractTransactionType enumerates four codes —
/// P, S, S (partial), and <b>S (full)</b>. A full-sale line must classify as
/// Sale, not be dropped: silently losing every "S (full)" disclosure would
/// under-report members liquidating entire positions.
/// </summary>
public class HouseDisclosureClientParseTransactionLinesFullSaleTests
{
    [Fact]
    public void ParseTransactionLines_FullSaleTypeCode_ClassifiesAsSale()
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
            new DateOnly(2025, 2, 14),
            "CA01"
        );

        var lines = new[]
        {
            "JT Nvidia Corp (NVDA) S (full) 02/10/2025 02/14/2025 $15,001 - $50,000",
        };

        var method = typeof(HouseDisclosureClient).GetMethod(
            "ParseTransactionLines",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var result = (List<DisclosureTransaction>)method.Invoke(client, [lines, filing]);

        result.Should().ContainSingle("the S (full) line is well-formed and must not be dropped");
        var sale = result.Single();
        sale.TransactionType.Should().Be(CongressTransactionType.Sale);
        sale.Ticker.Should().Be("NVDA");
        sale.OwnerType.Should().Be("JT");
        sale.TransactionDate.Should().Be(new DateOnly(2025, 2, 10));
    }
}
