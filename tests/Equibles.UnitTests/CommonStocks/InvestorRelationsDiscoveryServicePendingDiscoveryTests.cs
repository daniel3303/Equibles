using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.HostedService.Services;

namespace Equibles.UnitTests.CommonStocks;

public class InvestorRelationsDiscoveryServicePendingDiscoveryTests
{
    private static readonly DateTime Cutoff = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, true)] // never probed → always eligible
    [InlineData("2026-05-15", true)] // probed before the cutoff → cooldown elapsed
    [InlineData("2026-06-05", false)] // probed within the cooldown → backs off
    public void PendingDiscovery_CheckedAt_GatesOnCooldownCutoff(string checkedAt, bool expected)
    {
        // Contract: persistent misses back off — a stock probed within the cooldown
        // window is skipped, while never-probed stocks and stocks whose cooldown has
        // elapsed are eligible again.
        var stock = new CommonStock
        {
            Website = "https://acme.com",
            InvestorRelationsCheckedAt =
                checkedAt == null ? null : DateTime.Parse(checkedAt).ToUniversalTime(),
        };

        var eligible = InvestorRelationsDiscoveryService.PendingDiscovery(Cutoff).Compile();

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
        var stock = new CommonStock { Website = website, InvestorRelationsUrl = irUrl };

        var eligible = InvestorRelationsDiscoveryService.PendingDiscovery(Cutoff).Compile();

        eligible(stock).Should().Be(expected);
    }
}
