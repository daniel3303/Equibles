using Equibles.CommonStocks.HostedService.Services;
using Equibles.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.CommonStocks;

/// <summary>
/// Contract: the IR probe resolves entirely through the stealth sidecar — every candidate (and the
/// homepage crawl) is rendered, the first that validates as an IR page wins, and the probe reports a
/// classified outcome: <c>Found</c>, <c>NoIrPageFound</c> (every candidate was assessed and none was an
/// IR page), or <c>Inconclusive</c> (the engine was unavailable for a candidate, so a real IR page may
/// have been missed). With no sidecar configured it reports <c>NoIrPageFound</c>. There is no
/// plain-HTTP path.
/// </summary>
public class InvestorRelationsProbeClientTests
{
    private const string IrPage =
        "<html><head><title>Investor Relations - Acme</title></head>"
        + "<body><h1>Quarterly results</h1></body></html>";

    private const string NotIr =
        "<html><head><title>Page not found</title></head><body>404</body></html>";

    [Fact]
    public async Task Discover_SidecarRendersValidIrPage_ReturnsFoundWithCandidateUrl()
    {
        var stealth = EnabledStealth(_ => IrPage);

        var client = NewClient(stealth);

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Outcome.Should().Be(IrProbeOutcome.Found);
        result.Page!.Url.Should().Be("https://acme.com/investors");
    }

    [Fact]
    public async Task Discover_AllCandidatesRenderNonIrContent_IsConclusiveMiss()
    {
        // Every candidate (and the homepage) renders real content that doesn't validate — the site was
        // fully assessed and has no IR page. This is the only case that earns the escalating backoff.
        var stealth = EnabledStealth(_ => NotIr);

        var client = NewClient(stealth);

        var result = await client.Discover(
            "acme.com",
            ["investors", "ir"],
            ["ir"],
            CancellationToken.None
        );

        result.Outcome.Should().Be(IrProbeOutcome.NoIrPageFound);
        result.Page.Should().BeNull();
    }

    [Fact]
    public async Task Discover_HostDefinitivelyAbsentForAll_IsConclusiveMiss()
    {
        // DNS doesn't resolve / host unreachable for every candidate: assessed and definitively absent,
        // so re-probing won't help — a conclusive miss, not a transient one.
        var stealth = EnabledStealthResults(_ => StealthFetchResult.PageUnavailable);

        var client = NewClient(stealth);

        var result = await client.Discover(
            "acme.com",
            ["investors"],
            ["ir"],
            CancellationToken.None
        );

        result.Outcome.Should().Be(IrProbeOutcome.NoIrPageFound);
    }

    [Fact]
    public async Task Discover_EngineUnavailableForACandidate_IsInconclusive()
    {
        // The sidecar couldn't render a candidate (timeout / reaped sidecar). Nothing validated, but the
        // site wasn't fully assessed, so the answer is inconclusive — the caller must retry soon rather
        // than exile the stock on the multi-day miss schedule. This is the EBS regression: a reachable
        // IR page was missed only because the engine was momentarily unavailable.
        var stealth = EnabledStealthResults(url =>
            url == "https://acme.com/investors"
                ? StealthFetchResult.SidecarUnavailable
                : StealthFetchResult.Rendered(NotIr)
        );

        var client = NewClient(stealth);

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Outcome.Should().Be(IrProbeOutcome.Inconclusive);
        result.Page.Should().BeNull();
    }

    [Fact]
    public async Task Discover_ValidPageWinsEvenWhenAnotherCandidateWasTransient()
    {
        // A transient failure on one candidate must not prevent a later candidate from validating — a
        // found page always wins over an inconclusive signal.
        var stealth = EnabledStealthResults(url =>
            url == "https://acme.com/ir" ? StealthFetchResult.SidecarUnavailable
            : url == "https://acme.com/investors" ? StealthFetchResult.Rendered(IrPage)
            : StealthFetchResult.Rendered(NotIr)
        );

        var client = NewClient(stealth);

        var result = await client.Discover(
            "acme.com",
            ["ir", "investors"],
            [],
            CancellationToken.None
        );

        result.Outcome.Should().Be(IrProbeOutcome.Found);
        result.Page!.Url.Should().Be("https://acme.com/investors");
    }

    [Fact]
    public async Task Discover_NoSidecar_IsNoOpConclusiveMiss()
    {
        // No stealth browser configured: the probe can't get past bot walls, so it finds nothing (there
        // is no plain-HTTP fallback) and re-probing won't help — report a conclusive miss.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(false);

        var client = NewClient(stealth);

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Outcome.Should().Be(IrProbeOutcome.NoIrPageFound);
        await stealth.DidNotReceive().TryFetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Discover_SidecarThrows_DegradesToInconclusive()
    {
        // The sidecar render throws (e.g. a navigation timeout) instead of the contractual classified
        // result. The probe must degrade that to a transient inconclusive outcome — not a conclusive
        // miss — so the stock retries soon instead of being written off as having no IR page.
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .TryFetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<StealthFetchResult>(new TimeoutException("navigation timeout"))
            );

        var client = NewClient(stealth);

        IrProbeResult result = null;
        var act = async () =>
            result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        await act.Should().NotThrowAsync();
        result!.Outcome.Should().Be(IrProbeOutcome.Inconclusive);
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

        var client = NewClient(stealth);

        var result = await client.Discover(
            "acme.com",
            ["investors", "ir"],
            ["ir"],
            CancellationToken.None
        );

        result.Outcome.Should().Be(IrProbeOutcome.Found);
        result.Page!.Url.Should().Be("https://acme.com/en-us/investors");
    }

    [Fact]
    public async Task Discover_RenderHitsRateLimitInterstitial_IsInconclusiveAndCoolsHostDown()
    {
        // The render landed on a Cloudflare 1015 page. The probe must NOT treat that as a validated
        // page or a conclusive miss — it cools the host down and reports the attempt inconclusive so
        // the stock retries after the cooldown.
        const string rateLimited =
            "<html><head><title>Error 1015</title></head>"
            + "<body>You are being rate limited</body></html>";
        var stealth = EnabledStealth(_ => rateLimited);
        var gate = NewGate();

        var client = new InvestorRelationsProbeClient(
            stealth,
            gate,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Outcome.Should().Be(IrProbeOutcome.Inconclusive);
        gate.IsCoolingDown("https://acme.com/investors").Should().BeTrue();
    }

    [Fact]
    public async Task Discover_HostAlreadyCoolingDown_SkipsTheRenderEntirely()
    {
        // A host parked in cooldown is skipped without a render — no sidecar call — and reported
        // inconclusive so the stock waits out the cooldown rather than being written off.
        var stealth = EnabledStealth(_ => IrPage);
        var gate = NewGate();
        gate.RecordRateLimited("https://acme.com/investors");

        var client = new InvestorRelationsProbeClient(
            stealth,
            gate,
            NullLogger<InvestorRelationsProbeClient>.Instance
        );

        var result = await client.Discover("acme.com", ["investors"], [], CancellationToken.None);

        result.Outcome.Should().Be(IrProbeOutcome.Inconclusive);
        await stealth.DidNotReceive().TryFetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static InvestorRelationsProbeClient NewClient(IStealthBrowserClient stealth) =>
        new(stealth, NewGate(), NullLogger<InvestorRelationsProbeClient>.Instance);

    // A gate with no inter-request delay so tests don't pay the throttle wait.
    private static OutboundHostGate NewGate() =>
        new(
            Options.Create(new OutboundHostGateOptions { MinIntervalMilliseconds = 0 }),
            NullLogger<OutboundHostGate>.Instance
        );

    private static IStealthBrowserClient EnabledStealth(Func<string, string> bodyForUrl) =>
        EnabledStealthResults(url => StealthFetchResult.Rendered(bodyForUrl(url)));

    private static IStealthBrowserClient EnabledStealthResults(
        Func<string, StealthFetchResult> resultForUrl
    )
    {
        var stealth = Substitute.For<IStealthBrowserClient>();
        stealth.IsEnabled.Returns(true);
        stealth
            .TryFetchHtml(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(resultForUrl(ci.ArgAt<string>(0))));
        return stealth;
    }
}
