using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsDiscoveryServicePendingDiscoveryTests
{
    private static readonly DateTime Cutoff = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int CurrentVersion = 5;

    [Theory]
    [InlineData(null, true)] // never probed → always eligible
    [InlineData("2026-05-15", true)] // probed before the cutoff → cooldown elapsed
    [InlineData("2026-06-05", false)] // probed within the cooldown → backs off
    public void PendingDiscovery_CheckedAt_GatesOnCooldownCutoff(string checkedAt, bool expected)
    {
        // Contract: persistent misses back off — a stock probed within the cooldown window is
        // skipped, while never-probed stocks and stocks whose cooldown has elapsed are eligible.
        // Version is current and the website wasn't found after the probe, so the cooldown clause
        // is isolated.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt =
                checkedAt == null ? null : DateTime.Parse(checkedAt).ToUniversalTime(),
            InvestorRelationsDiscoveryVersion = CurrentVersion,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Cutoff, CurrentVersion)
            .Compile();

        eligible(stock).Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, false)] // no website → nothing to probe
    [InlineData("", null, false)] // empty website → nothing to probe
    [InlineData("https://acme.com", "https://ir.acme.com", false)] // already discovered
    public void PendingDiscovery_WebsiteAndIrUrl_ExcludeNonCandidates(
        string website,
        string irUrl,
        bool expected
    )
    {
        var stock = new CommonStock
        {
            Website = website,
            InvestorRelationsUrl = irUrl,
            InvestorRelationsDiscoveryVersion = CurrentVersion,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Cutoff, CurrentVersion)
            .Compile();

        eligible(stock).Should().Be(expected);
    }

    [Theory]
    [InlineData(CurrentVersion - 1, true)] // probed under an older generation → re-sweep now
    [InlineData(CurrentVersion, false)] // already at the current generation → wait for the cooldown
    public void PendingDiscovery_OlderVersion_IsEligibleWithinCooldown(int version, bool expected)
    {
        // Contract: a probe-logic improvement (version bump) re-opens the backlog of misses
        // immediately, bypassing the cooldown — even a stock probed moments ago is reconsidered when
        // it was probed under an older version.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), // within cooldown
            WebsiteCheckedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
            InvestorRelationsDiscoveryVersion = version,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Cutoff, CurrentVersion)
            .Compile();

        eligible(stock).Should().Be(expected);
    }

    [Fact]
    public void PendingDiscovery_WebsiteFoundAfterLastProbe_IsEligibleWithinCooldown()
    {
        // Contract: the reconciliation backstop for the website-discovered cascade — a stock probed
        // (and missed) BEFORE its website was found is re-probed even within the cooldown and at the
        // current version, because the input it needs only arrived afterwards.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc),
            WebsiteCheckedAt = new DateTime(2026, 6, 6, 0, 0, 0, DateTimeKind.Utc), // website found later
            InvestorRelationsDiscoveryVersion = CurrentVersion,
        };

        var eligible = InvestorRelationsDiscoveryService
            .PendingDiscovery(Cutoff, CurrentVersion)
            .Compile();

        eligible(stock).Should().BeTrue();
    }
}
