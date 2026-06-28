using Equibles.CommonStocks.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: the IR probe resolves entirely through the stealth sidecar — every candidate (and the
/// homepage crawl) is rendered, the first that validates as an IR page wins, a render failure is a
/// miss, and with no sidecar configured the probe is a no-op. There is no plain-HTTP path.
/// </summary>
public class InvestorRelationsProbeClientTests
{
    private const string IrPage =
        "<html><head><title>Investor Relations - Acme</title></head>"
        + "<body><h1>Quarterly results</h1></body></html>";

    private const string NotIr =
        "<html><head><title>Page not found</title></head><body>404</body></html>";

    [Fact]
    public async Task Discover_SidecarRendersValidIrPage_ReturnsCandidateUrl()
    {
        var stealth = EnabledStealth(_ => IrPage);

        var client = new InvestorRelationsProbeClient(
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://acme.com/investors");
    }

    [Fact]
    public async Task Discover_NoSidecar_IsNoOp()
    {
        // No stealth browser configured: the probe can't get past bot walls, so it finds nothing
        // (there is no plain-HTTP fallback).
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);

        var client = new InvestorRelationsProbeClient(
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Should().BeNull();
        await stealth.DidNotReceive().FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discover_SidecarThrows_DegradesToMiss()
    {
        // The sidecar render throws (e.g. a navigation timeout) instead of the contractual null. The
        // probe must degrade that to a miss — if it bubbles out, the caller skips the definitive-miss
        // back-off, the stock is never stamped, re-occupies every batch, and starves the universe.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string>(new TimeoutException("navigation timeout")));

        var client = new InvestorRelationsProbeClient(
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        IrDiscoveryResult result = null;
        var act = async () =>
            result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        await act.Should().NotThrowAsync();
        result.Should().BeNull();
    }

    [Fact]
    public async Task Discover_GuessedPathsMiss_FollowsHomepageIrLink()
    {
        // The guessed paths/subdomains don't validate, but the homepage links to the real IR page at
        // a non-guessed location (/en-us/investors) — the homepage crawl follows it.
        const string homepage =
            "<html><head><title>Acme Corp</title></head><body>"
            + "<a href=\"/en-us/investors\">Investors</a></body></html>";

        var stealth = EnabledStealth(url =>
            url == "https://acme.com" ? homepage
            : url == "https://acme.com/en-us/investors" ? IrPage
            : NotIr
        );

        var client = new InvestorRelationsProbeClient(
            stealth,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover(
            "acme.com",
            ["investors", "ir"],
            ["ir"],
            CancellationToken.None
        );

        result.Should().NotBeNull();
        result!.Url.Should().Be("https://acme.com/en-us/investors");
    }

    private static IStealthBrowserClient EnabledStealth(Func<string, string> bodyForUrl)
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .FetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(bodyForUrl(ci.ArgAt<string>(0))));
        return stealth;
    }
}
