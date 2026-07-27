using System.Reflection;
using System.Runtime.CompilerServices;
using Equibles.Yahoo.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Yahoo;

/// <summary>
/// Tests for the upstream-outage warning in <see cref="YahooPriceImportService"/>, exercised via
/// reflection on the private <c>WarnIfUpstreamServedNothing</c>.
///
/// The warning's first version compared barren fetches against the cycle's fetches alone, which is
/// a ratio a HEALTHY quiet cycle maxes out: once the active set is current, the only stocks still
/// fetched are the dormant tail and the thin lines that did not trade (~600 of ~8,400), and every
/// one returns nothing by design — so it would have fired on most cycles of every normal day and
/// been filtered as noise within a week. The signature that separates a real outage is SIZE: on
/// 2026-07-24 ~73% of the universe fetched and inserted nothing. These pin the size condition.
/// </summary>
public class YahooPriceImportServiceOutageWarningTests
{
    private static readonly MethodInfo WarnMethod = typeof(YahooPriceImportService).GetMethod(
        "WarnIfUpstreamServedNothing",
        BindingFlags.NonPublic | BindingFlags.Instance
    );

    private static int Warn(int universeSize, int fetched, int fetchedWithNothingNew)
    {
        var logger = Substitute.For<ILogger<YahooPriceImportService>>();
        var service = (YahooPriceImportService)
            RuntimeHelpers.GetUninitializedObject(typeof(YahooPriceImportService));
        typeof(YahooPriceImportService)
            .GetField("_logger", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, logger);

        WarnMethod.Invoke(service, [universeSize, fetched, fetchedWithNothingNew]);

        return logger
            .ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ILogger.Log));
    }

    [Fact]
    public void TheRealOutageSignature_Fires()
    {
        // 2026-07-24 as measured: ~6,100 fetched, ~6,100 barren, of an 8,402-stock universe (73%).
        Warn(8402, 6100, 6100).Should().Be(1);
    }

    [Fact]
    public void AHealthyQuietCycle_DoesNotFire()
    {
        // The dormant-plus-thin tail: every fetch barren (ratio 1.0), but only ~7% of the universe.
        // The ratio test alone fired here ~9 times a day — the exact false positive being pinned.
        Warn(8402, 620, 620).Should().Be(0);
    }

    [Fact]
    public void TheFirstCycleOfANormalDay_DoesNotFire()
    {
        // Post-rollover, the whole universe fetches and ~92% inserts; barren ratio is far below
        // the threshold even though the fetch count is huge.
        Warn(8402, 8000, 620).Should().Be(0);
    }

    [Fact]
    public void ASmallCrawl_IsNeverEvidence()
    {
        // A tiny configured universe or a weekend no-op can be 100% barren without meaning anything.
        Warn(150, 150, 150).Should().Be(0);
    }

    [Fact]
    public void AnEmptyUniverse_NeverDividesByZero()
    {
        Warn(0, 0, 0).Should().Be(0);
    }
}
