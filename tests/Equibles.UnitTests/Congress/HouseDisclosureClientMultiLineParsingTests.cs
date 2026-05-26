using System.Reflection;
using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Models;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientMultiLineParsingTests
{
    [Fact]
    public void ParseTransactionLines_MultiLineEntries_JoinsAndParsesCorrectly()
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
            "Nancy Pelosi",
            "20026590",
            new DateOnly(2025, 1, 17),
            "CA11"
        );

        // Real House PTR PDF format: asset name wraps onto the next line,
        // ticker + type + dates follow on the continuation line.
        var lines = new[]
        {
            "P T R",
            "Name: Hon. Nancy Pelosi",
            "SP Alphabet Inc. - Class A Common",
            "Stock (GOOGL)  [OP]P 01/14/2025 01/14/2025 $250,001 -",
            "$500,000",
            "SP Apple Inc. - Common Stock (AAPL)",
            "[ST]S (partial) 12/31/2024 12/31/2024 $5,000,001 -",
            "$25,000,000",
            "SP NVIDIA Corporation - Common",
            "Stock (NVDA)  [ST]S (partial) 12/31/2024 12/31/2024 $1,000,001 -",
            "$5,000,000",
        };

        var method = typeof(HouseDisclosureClient).GetMethod(
            "ParseTransactionLines",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var result = (List<DisclosureTransaction>)method.Invoke(client, [lines, filing]);

        result.Should().HaveCount(3);

        var googl = result.Single(t => t.Ticker == "GOOGL");
        googl.TransactionType.Should().Be(CongressTransactionType.Purchase);
        googl.OwnerType.Should().Be("SP");
        googl.TransactionDate.Should().Be(new DateOnly(2025, 1, 14));
        googl.AmountFrom.Should().Be(250_001);
        googl.AmountTo.Should().Be(500_000);

        var aapl = result.Single(t => t.Ticker == "AAPL");
        aapl.TransactionType.Should().Be(CongressTransactionType.Sale);
        aapl.TransactionDate.Should().Be(new DateOnly(2024, 12, 31));
        aapl.AmountFrom.Should().Be(5_000_001);
        aapl.AmountTo.Should().Be(25_000_000);

        var nvda = result.Single(t => t.Ticker == "NVDA");
        nvda.TransactionType.Should().Be(CongressTransactionType.Sale);
        nvda.TransactionDate.Should().Be(new DateOnly(2024, 12, 31));
        nvda.AmountFrom.Should().Be(1_000_001);
        nvda.AmountTo.Should().Be(5_000_000);
    }

    [Fact]
    public void JoinMultiLineEntries_MixedSingleAndMultiLine_JoinsCorrectly()
    {
        var lines = new[]
        {
            "Header line without owner code",
            "SP Apple Inc. (AAPL) P 01/14/2025 01/20/2025 $1,001 - $15,000",
            "JT Alphabet Inc. - Class A Common",
            "Stock (GOOGL)  [OP]P 01/14/2025 01/14/2025 $250,001 -",
            "$500,000",
            "Footer text",
            "DC Tesla Inc (TSLA) S 03/01/2025 03/05/2025 $50,001 - $100,000",
        };

        var result = HouseDisclosureClient.JoinMultiLineEntries(lines);

        result.Should().HaveCount(3);
        result[0].Should().Be("SP Apple Inc. (AAPL) P 01/14/2025 01/20/2025 $1,001 - $15,000");
        result[1]
            .Should()
            .Be(
                "JT Alphabet Inc. - Class A Common Stock (GOOGL)  [OP]P 01/14/2025 01/14/2025 $250,001 - $500,000 Footer text"
            );
        result[2].Should().Be("DC Tesla Inc (TSLA) S 03/01/2025 03/05/2025 $50,001 - $100,000");
    }
}
