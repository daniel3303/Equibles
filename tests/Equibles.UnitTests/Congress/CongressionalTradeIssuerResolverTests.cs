using Equibles.CommonStocks.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeIssuerResolverTests
{
    private static readonly Guid OriginalIssuer = Guid.NewGuid();
    private static readonly Guid ReusedIssuer = Guid.NewGuid();

    [Fact]
    public void ResolveAtDate_SameIssuerBracketsTrade_ResolvesIssuer()
    {
        var evidence = new[]
        {
            Evidence(OriginalIssuer, new DateOnly(2021, 2, 1)),
            Evidence(OriginalIssuer, new DateOnly(2021, 8, 1)),
        };

        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            evidence,
            new DateOnly(2021, 5, 1)
        );

        result.Should().Be(OriginalIssuer);
    }

    [Fact]
    public void ResolveAtDate_ExactAuthoritativeObservation_ResolvesIssuer()
    {
        var evidence = new[] { Evidence(OriginalIssuer, new DateOnly(2021, 5, 1)) };

        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            evidence,
            new DateOnly(2021, 5, 1)
        );

        result.Should().Be(OriginalIssuer);
    }

    [Fact]
    public void ResolveAtDate_TickerReuseBracketsTrade_RefusesLink()
    {
        var evidence = new[]
        {
            Evidence(OriginalIssuer, new DateOnly(2021, 2, 1)),
            Evidence(ReusedIssuer, new DateOnly(2021, 8, 1)),
        };

        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            evidence,
            new DateOnly(2021, 5, 1)
        );

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveAtDate_OnlyOneSideObserved_RefusesLink()
    {
        var evidence = new[] { Evidence(OriginalIssuer, new DateOnly(2021, 2, 1)) };

        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            evidence,
            new DateOnly(2021, 5, 1)
        );

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveAtDate_CompetingIssuersOnExactDate_RefusesLink()
    {
        var date = new DateOnly(2021, 5, 1);
        var evidence = new[]
        {
            Evidence(OriginalIssuer, new DateOnly(2021, 2, 1)),
            Evidence(OriginalIssuer, date),
            Evidence(ReusedIssuer, date),
            Evidence(OriginalIssuer, new DateOnly(2021, 8, 1)),
        };

        var result = CongressionalTradeIssuerResolver.ResolveAtDate(evidence, date);

        result.Should().BeNull();
    }

    private static CommonStockTickerEvidence Evidence(Guid issuerId, DateOnly filedDate) =>
        new()
        {
            CommonStockId = issuerId,
            Ticker = "GOLD",
            FiledDate = filedDate,
            SourceDocumentId = Guid.NewGuid(),
            AccessionNumber = Guid.NewGuid().ToString("N"),
        };
}
