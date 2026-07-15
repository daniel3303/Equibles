using System.Reflection;
using Equibles.Yahoo.HostedService;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Pins <c>YahooPriceScraperWorker.IsEnrichmentDue</c>, the rule that decides which price cycles
/// carry the key-statistics + company-profile calls (2 extra Yahoo calls per stock — the bulk of
/// a cycle's traffic). Price cycles run frequently for fresh closes; enrichment must ride along
/// only once per configured interval. Both failure directions matter: enriching every fast cycle
/// multiplies Yahoo traffic ~12x (rate-limit bans), while never enriching lets market cap and
/// shares outstanding go permanently stale.
/// </summary>
public class YahooPriceScraperWorkerEnrichmentCadenceTests
{
    private static readonly MethodInfo IsEnrichmentDueMethod =
        typeof(YahooPriceScraperWorker).GetMethod(
            "IsEnrichmentDue",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static bool IsEnrichmentDue(
        DateTime lastEnrichmentAt,
        DateTime now,
        TimeSpan enrichmentInterval
    ) => (bool)IsEnrichmentDueMethod.Invoke(null, [lastEnrichmentAt, now, enrichmentInterval]);

    private static readonly DateTime Now = new(2026, 7, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    [Fact]
    public void IsEnrichmentDue_NeverEnriched_IsDue()
    {
        // A fresh worker (default stamp) must enrich on its first cycle — a restart may only
        // delay enrichment, never skip it.
        IsEnrichmentDue(default, Now, Interval).Should().BeTrue();
    }

    [Fact]
    public void IsEnrichmentDue_IntervalNotElapsed_IsNotDue()
    {
        // A fast price cycle inside the interval must stay prices-only, or every 2h cycle carries
        // the full ~17k enrichment calls and Yahoo rate-limits the lane.
        IsEnrichmentDue(Now.AddHours(-2), Now, Interval).Should().BeFalse();
    }

    [Fact]
    public void IsEnrichmentDue_IntervalElapsed_IsDue()
    {
        // At or past the interval the cycle must enrich again, or key stats go stale forever.
        IsEnrichmentDue(Now.AddHours(-24), Now, Interval).Should().BeTrue();
        IsEnrichmentDue(Now.AddHours(-30), Now, Interval).Should().BeTrue();
    }
}
