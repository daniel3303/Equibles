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

    // The cohort the directory exists for: a trade after the issuer's latest cover page and before
    // the next one, with the directory listing the ticker under the same CIK afterwards.
    [Fact]
    public void ResolveAtDate_TradeAfterLatestCoverPage_LinksThroughALaterDirectorySpan()
    {
        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            [Evidence(OriginalIssuer, new DateOnly(2026, 9, 10))],
            [Span(OriginalIssuer, new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 25))],
            new DateOnly(2026, 9, 16)
        );

        result.Should().Be(OriginalIssuer);
    }

    [Fact]
    public void ResolveAtDate_DirectoryNamesAnotherIssuerAfterTheCoverPage_RefusesTheHandover()
    {
        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            [Evidence(OriginalIssuer, new DateOnly(2026, 9, 10))],
            [Span(ReusedIssuer, new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21))],
            new DateOnly(2026, 9, 16)
        );

        result.Should().BeNull();
    }

    // A directory CIK the store carries no issuer for still names someone else.
    [Fact]
    public void ResolveAtDate_DirectoryCikWithoutAnIssuer_RefusesTheLink()
    {
        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            [Evidence(OriginalIssuer, new DateOnly(2026, 9, 10))],
            [Span(null, new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 21))],
            new DateOnly(2026, 9, 16)
        );

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveAtDate_TradeInsideADirectorySpan_LinksWithoutACoverPage()
    {
        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            [],
            [Span(OriginalIssuer, new DateOnly(2026, 10, 21), new DateOnly(2026, 12, 1))],
            new DateOnly(2026, 11, 5)
        );

        result.Should().Be(OriginalIssuer);
    }

    [Fact]
    public void ResolveAtDate_TradeBetweenTwoOwnersDirectorySpans_RefusesTheLink()
    {
        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            [],
            [
                Span(OriginalIssuer, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10)),
                Span(ReusedIssuer, new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 20)),
            ],
            new DateOnly(2026, 10, 11)
        );

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveAtDate_SpanAndCoverPageDisagreeOnTheTradeDate_RefusesTheLink()
    {
        var date = new DateOnly(2026, 11, 5);
        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            [Evidence(ReusedIssuer, date)],
            [Span(OriginalIssuer, new DateOnly(2026, 10, 21), new DateOnly(2026, 12, 1))],
            date
        );

        result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResolveAtDate_NoEvidenceAfterTheTrade_StaysUnlinked(bool directoryBefore)
    {
        CongressionalTradeIssuerResolver.DirectorySpan[] directory = directoryBefore
            ? [Span(OriginalIssuer, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31))]
            : [];

        var result = CongressionalTradeIssuerResolver.ResolveAtDate(
            [Evidence(OriginalIssuer, new DateOnly(2026, 7, 1))],
            directory,
            new DateOnly(2026, 9, 16)
        );

        result.Should().BeNull();
    }

    private static CongressionalTradeIssuerResolver.DirectorySpan Span(
        Guid? issuerId,
        DateOnly first,
        DateOnly last
    ) => new("GOLD", issuerId, first, last);

    private static EquityIssuerTickerEvidence Evidence(Guid issuerId, DateOnly filedDate) =>
        new()
        {
            EquityIssuerId = issuerId,
            Ticker = "GOLD",
            FiledDate = filedDate,
            SourceDocumentId = Guid.NewGuid(),
            AccessionNumber = Guid.NewGuid().ToString("N"),
        };
}
