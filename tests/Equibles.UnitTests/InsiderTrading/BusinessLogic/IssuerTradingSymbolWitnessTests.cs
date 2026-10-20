using Equibles.InsiderTrading.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.UnitTests.InsiderTrading.BusinessLogic;

/// <summary>
/// The fixture is Kellanova's last Form 4 (accession 0001193125-25-317636, filed 2025-12-12 by
/// the W.K. Kellogg Foundation Trust), the filing that names its delisted symbol after the
/// symbol left SEC's active ticker file.
/// </summary>
public class IssuerTradingSymbolWitnessTests
{
    private static readonly string KellanovaForm4 = File.ReadAllText(
        Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "InsiderTrading",
            "form4-kellanova-0001193125-25-317636.txt"
        )
    );

    private const string OtherIssuerForm4 = """
        <XML>
        <ownershipDocument>
            <issuer>
                <issuerCik>0000012345</issuerCik>
                <issuerName>OTHER CO</issuerName>
                <issuerTradingSymbol>OTH</issuerTradingSymbol>
            </issuer>
        </ownershipDocument>
        </XML>
        """;

    [Fact]
    public void GetIssuerTradingSymbol_IssuerOwnFiling_ReturnsItsSymbol()
    {
        InsiderFilingParser.GetIssuerTradingSymbol(KellanovaForm4, "55067").Should().Be("K");
        InsiderFilingParser.GetIssuerTradingSymbol(KellanovaForm4, "0000055067").Should().Be("K");
    }

    [Fact]
    public void GetIssuerTradingSymbol_FilingAboutAnotherIssuer_ReturnsNull()
    {
        // The same document sits in the reporting owner's feed; it says nothing about that CIK.
        InsiderFilingParser.GetIssuerTradingSymbol(KellanovaForm4, "1197921").Should().BeNull();
    }

    [Fact]
    public async Task GetNewestStatedSymbol_SkipsFilingsAboutOtherIssuers()
    {
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetCompanyFilings("55067", null, new DateOnly(2023, 12, 12), null)
            .Returns([
                Filing("0000055067-25-000001", "4", new DateOnly(2025, 12, 15)),
                Filing("0001193125-25-317636", "4", new DateOnly(2025, 12, 12)),
                Filing("0000055067-25-000002", "10-K", new DateOnly(2025, 12, 20)),
            ]);
        client
            .GetDocumentContent("0000055067-25-000001", "55067", Arg.Any<CancellationToken>())
            .Returns(OtherIssuerForm4);
        client
            .GetDocumentContent("0001193125-25-317636", "55067", Arg.Any<CancellationToken>())
            .Returns(KellanovaForm4);
        var witness = new IssuerTradingSymbolWitness(
            client,
            NullLogger<IssuerTradingSymbolWitness>.Instance
        );

        var symbol = await witness.GetNewestStatedSymbol(
            "55067",
            new DateOnly(2023, 12, 12),
            CancellationToken.None
        );

        symbol.Should().Be("K");
        await client
            .DidNotReceive()
            .GetDocumentContent(
                "0000055067-25-000002",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GetNewestStatedSymbol_EdgarUnavailable_ReturnsNull()
    {
        var client = Substitute.For<ISecEdgarClient>();
        client
            .GetCompanyFilings("55067", null, Arg.Any<DateOnly?>(), null)
            .ThrowsAsync(new HttpRequestException("503"));
        var witness = new IssuerTradingSymbolWitness(
            client,
            NullLogger<IssuerTradingSymbolWitness>.Instance
        );

        (await witness.GetNewestStatedSymbol("55067", new DateOnly(2023, 12, 12)))
            .Should()
            .BeNull();
    }

    private static FilingData Filing(string accession, string form, DateOnly filed) =>
        new()
        {
            Cik = "55067",
            AccessionNumber = accession,
            Form = form,
            FilingDate = filed,
        };
}
