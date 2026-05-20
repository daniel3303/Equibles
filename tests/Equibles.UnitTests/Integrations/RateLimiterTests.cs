using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Equibles.Integrations.Common.RateLimiter;

namespace Equibles.UnitTests.Integrations;

public class RateLimiterTests
{
    [Fact]
    public async Task RequestsWithinLimit_CompleteImmediately()
    {
        // Arrange: allow 5 requests per 100ms window
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(100));
        var sw = Stopwatch.StartNew();

        // Act: make 3 requests (well within the limit of 5)
        for (var i = 0; i < 3; i++)
        {
            await limiter.WaitAsync();
        }

        sw.Stop();

        // Assert: should complete nearly instantly (under 50ms)
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public void DefaultConstructor_SetsExpectedDefaults()
    {
        // Arrange & Act
        var limiter = new RateLimiter();

        // Assert: default is 5 requests per 1 minute — just verify construction succeeds
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }

    [Fact]
    public void CustomConstructorValues_AreAccepted()
    {
        // Arrange & Act
        var limiter = new RateLimiter(maxRequests: 10, timeWindow: TimeSpan.FromSeconds(30));

        // Assert: construction with custom values succeeds
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }

    [Fact]
    public async Task PauseFor_DelaysSubsequentWaitAsyncCalls()
    {
        // Arrange: generous limit so throttling comes only from PauseFor
        var limiter = new RateLimiter(maxRequests: 100, timeWindow: TimeSpan.FromSeconds(10));

        // Act: pause for 200ms, then measure how long WaitAsync takes
        limiter.PauseFor(TimeSpan.FromMilliseconds(200));

        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        // Assert: should have waited at least ~150ms (allowing tolerance)
        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(150);
    }

    [Fact]
    public async Task MultipleRapidRequests_WithinLimit_AllSucceedQuickly()
    {
        // Arrange: allow 5 requests in a 500ms window
        var limiter = new RateLimiter(maxRequests: 5, timeWindow: TimeSpan.FromMilliseconds(500));
        var sw = Stopwatch.StartNew();

        // Act: fire 5 requests concurrently (exactly at the limit)
        var tasks = Enumerable.Range(0, 5).Select(_ => limiter.WaitAsync()).ToArray();
        await Task.WhenAll(tasks);

        sw.Stop();

        // Assert: all 5 should complete quickly since they're within the limit
        sw.ElapsedMilliseconds.Should().BeLessThan(50);
    }

    [Fact]
    public async Task WaitAsync_FillsCapacity_NextRequestWaitsForOldestToAgeOut()
    {
        // RateLimiter is the only thing standing between our scrapers and rate-limit bans
        // from FRED, FINRA, Yahoo, CBOE, and CFTC. Its core function is throttling: when
        // the in-window request count reaches `maxRequests`, the next WaitAsync MUST block
        // until the oldest request ages out of the window. The existing tests cover the
        // "within capacity" path (everything fast) and PauseFor (external 429 reaction)
        // — neither exercises the capacity-throttle branch in CalculateWaitTime where
        // `_requestTimes.Count >= _maxRequests` returns the positive remainder time.
        //
        // The risk this test pins: a refactor that simplifies CalculateWaitTime to always
        // return TimeSpan.Zero (or that inverts the `Count < _maxRequests` predicate, or
        // that drops the recursive `await WaitAsync()` at the end of WaitAsync) would
        // let through unlimited requests at the speed of the await machinery. None of the
        // existing tests would fail — they all use maxRequests sized comfortably above
        // their request count. The first sign of the regression would be production bans
        // from the rate-limited APIs, with no visible test failure in CI.
        //
        // Construction: capacity=2 in a 300ms window. Fire 2 requests immediately to fill
        // the queue, then time the 3rd. The 3rd must wait long enough for the first
        // request to age out — at least ~200ms (300ms window minus the small gap between
        // requests one and three).
        var limiter = new RateLimiter(maxRequests: 2, timeWindow: TimeSpan.FromMilliseconds(300));
        await limiter.WaitAsync();
        await limiter.WaitAsync();

        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(200);
    }

    [Fact]
    public async Task PauseFor_ShorterDurationFollowingLongerPause_DoesNotShortenExistingPause()
    {
        // PauseFor's body holds a guard: `if (newPauseUntil > _pauseUntil) _pauseUntil = ...;`
        // The guard is load-bearing for cascading rate-limit backoffs. Concrete production
        // scenario: SEC EDGAR returns a 429 with Retry-After=300s, SendWithRetryAsync calls
        // PauseFor(5min). Some time later another worker on the same shared RateLimiter gets
        // a less-aggressive 429 (Retry-After=1s) — without the guard, the second
        // PauseFor(1s) would OVERWRITE the still-active 5-minute pause, releasing every
        // queued request after only one second of the SEC-mandated 5-minute cooldown.
        // The next request burst would hit SEC's load balancer before the cooldown
        // expires, escalate to a longer ban, and the scraper would spiral into ever-
        // increasing 429 windows.
        //
        // The existing PauseFor_DelaysSubsequentWaitAsyncCalls test only exercises a single
        // PauseFor in isolation, so it can't catch a refactor that drops the if-guard. A
        // "simplification" PR that wrote `_pauseUntil = newPauseUntil;` unconditionally
        // (or `_pauseUntil = Max(...)` with a wrong comparison) would pass every existing
        // pin and silently degrade rate-limit protection in production.
        //
        // Construction: a generous in-window capacity so request-rate throttling can't
        // confound the pause assertion. Issue a long PauseFor (500ms), immediately follow
        // with a shorter PauseFor (50ms), then time WaitAsync. The assertion is that
        // WaitAsync blocks for at least most of the long pause — proves the longer pause
        // survived. A regression that lets the shorter pause win would complete in ~50ms,
        // far below the 350ms threshold.
        var limiter = new RateLimiter(maxRequests: 100, timeWindow: TimeSpan.FromSeconds(10));

        limiter.PauseFor(TimeSpan.FromMilliseconds(500));
        limiter.PauseFor(TimeSpan.FromMilliseconds(50));

        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(350);
    }

    [Fact]
    public void Constructor_WithCustomTimeWindow_Succeeds()
    {
        // Arrange & Act
        var limiter = new RateLimiter(maxRequests: 3, timeWindow: TimeSpan.FromMilliseconds(100));

        // Assert
        limiter.Should().NotBeNull();
        limiter.Should().BeAssignableTo<IRateLimiter>();
    }

    [Fact]
    public void WaitAsync_StateMachine_DoesNotCallWaitAsyncRecursively()
    {
        // Bug #1273: WaitAsync was written as `await Task.Delay(waitTime); await WaitAsync();`.
        // Each saturated iteration boxed a fresh state machine and registered an inline
        // continuation onto the cascade. When the leaf iteration finally completed, the
        // chain of continuations unwound synchronously on a single stack — one MoveNext
        // frame per iteration — and under sustained saturation (reproducible by the full
        // integration test suite) the cascade overflowed the stack and crashed the host.
        //
        // A direct stress-test cannot pin this contract: StackOverflowException is
        // uncatchable and would terminate the test host, polluting unrelated tests.
        // Instead the test inspects the compiler-generated state machine for WaitAsync
        // and asserts the structural property that makes the cascade impossible — its
        // MoveNext must not contain a call back into WaitAsync. Replacing the recursion
        // with a `while` loop removes that call and is the canonical fix; a future PR
        // re-introducing `await WaitAsync()` (or any other self-call within the body)
        // re-introduces the same unbounded cascade and trips this regression.
        var waitAsync = typeof(RateLimiter).GetMethod(nameof(RateLimiter.WaitAsync));
        waitAsync.Should().NotBeNull();

        var stateMachineAttr = waitAsync.GetCustomAttribute<AsyncStateMachineAttribute>();
        stateMachineAttr
            .Should()
            .NotBeNull(
                "WaitAsync should be an async method with a compiler-generated state machine"
            );

        var moveNext = stateMachineAttr.StateMachineType.GetMethod(
            "MoveNext",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
        );
        moveNext.Should().NotBeNull();

        var body = moveNext.GetMethodBody();
        body.Should().NotBeNull();
        var il = body.GetILAsByteArray();
        il.Should().NotBeNull();

        // The compiler emits a `call`/`callvirt` to WaitAsync using its MethodDef token
        // (same module). Scanning the IL for the 4-byte token detects the self-call
        // without depending on full IL-disassembly machinery; tokens are 4-byte aligned
        // operand values, so false positives in well-formed IL are negligible.
        var token = waitAsync.MetadataToken;
        var tokenBytes = BitConverter.GetBytes(token);
        var selfCallFound = false;
        for (var i = 0; i <= il.Length - 4; i++)
        {
            if (
                il[i] == tokenBytes[0]
                && il[i + 1] == tokenBytes[1]
                && il[i + 2] == tokenBytes[2]
                && il[i + 3] == tokenBytes[3]
            )
            {
                selfCallFound = true;
                break;
            }
        }

        selfCallFound
            .Should()
            .BeFalse(
                "WaitAsync's state machine must not call WaitAsync — recursion grows the inline completion cascade by one frame per iteration and overflowed the stack under sustained saturation (bug #1273)"
            );
    }

    [Fact]
    public async Task ConcurrentCallers_ExceedingCapacity_HonorRatePerWindow()
    {
        // RateLimiter's core contract — "at most `maxRequests` releases per `timeWindow`"
        // — is the only thing standing between this app's scrapers and rate-limit bans
        // from FRED, FINRA, Yahoo, CBOE, SEC, and CFTC. The existing concurrent test
        // (MultipleRapidRequests_WithinLimit_AllSucceedQuickly) covers exactly `maxRequests`
        // callers in flight; nothing pins behavior under EXCESS concurrent load, which is
        // the realistic case when several scrapers share a limiter or a burst hits at once.
        //
        // The risk this test pins: a refactor of the WaitAsync while-loop (introduced by
        // bug #1273's fix) that re-orders the lock/await pair — releasing the lock too late,
        // skipping the post-Task.Delay re-check, or simplifying CalculateWaitTime — could
        // let bursts past the contracted ceiling. None of the existing tests would catch it;
        // CI would stay green and the first sign would be a production ban from upstream.
        //
        // Six concurrent callers against capacity=2 in a 200ms window. Every sliding window
        // of three consecutive completions must span at least the timeWindow, since at the
        // time the third was permitted the first was still the oldest in the queue and the
        // limiter must have waited until oldest + timeWindow before allowing the enqueue.
        const int maxRequests = 2;
        var timeWindow = TimeSpan.FromMilliseconds(200);
        var limiter = new RateLimiter(maxRequests: maxRequests, timeWindow: timeWindow);

        var completions = new ConcurrentQueue<long>();
        var clock = Stopwatch.StartNew();

        var tasks = Enumerable
            .Range(0, 6)
            .Select(async _ =>
            {
                await limiter.WaitAsync();
                completions.Enqueue(clock.ElapsedMilliseconds);
            })
            .ToArray();

        await Task.WhenAll(tasks);

        var ordered = completions.OrderBy(t => t).ToArray();
        ordered.Should().HaveCount(6);

        // Tolerance absorbs the small gap between the limiter's internal enqueue and the
        // test's clock read after `await WaitAsync()` returns. The contract is `>= timeWindow`;
        // the asserted lower bound is `timeWindow - 25ms` to remain robust to scheduling
        // jitter on slow CI runners without losing power against a real burst-past-cap regression.
        var lowerBoundMs = (long)timeWindow.TotalMilliseconds - 25;
        for (var i = 0; i + maxRequests < ordered.Length; i++)
        {
            var spanMs = ordered[i + maxRequests] - ordered[i];
            spanMs
                .Should()
                .BeGreaterThanOrEqualTo(
                    lowerBoundMs,
                    $"completions[{i}..{i + maxRequests}] must span ≥ ~{timeWindow.TotalMilliseconds}ms — at the moment the (maxRequests+1)-th release enqueued, the first of the triple was still the oldest entry and the limiter must have waited until it aged out of the window"
                );
        }
    }

    [Fact]
    public async Task WaitAsync_PauseShorterThanQueueWait_UsesQueueWait()
    {
        // WaitAsync's wait is `max(queue saturation wait, pause remaining)` — implemented
        // by the `if (pauseRemaining > waitTime) waitTime = pauseRemaining;` guard. Two
        // halves of that contract are already pinned: PauseFor_DelaysSubsequentWaitAsyncCalls
        // (queue=0, pause>0 → use pause) and WaitAsync_FillsCapacity_NextRequestWaitsForOldestToAgeOut
        // (queue>0, pause<0 → use queue). The remaining cell — both positive, pause < queue
        // — is unpinned, and is exactly the case a "simplification" PR could regress by
        // dropping the if-guard and writing `waitTime = pauseRemaining;` unconditionally.
        // That refactor would replace the ~400ms queue wait below with a ~50ms pause wait,
        // releasing the scraper before the upstream window allows it and triggering bans.
        //
        // Construction: maxRequests=1 saturates after the first call so the second WaitAsync
        // sees a positive queue-driven wait of ~400ms. A short PauseFor of 50ms sits BELOW
        // that — the longer (queue) wait must dominate, so total time is ~400ms, never ~50ms.
        var limiter = new RateLimiter(maxRequests: 1, timeWindow: TimeSpan.FromMilliseconds(400));
        await limiter.WaitAsync();

        limiter.PauseFor(TimeSpan.FromMilliseconds(50));

        var sw = Stopwatch.StartNew();
        await limiter.WaitAsync();
        sw.Stop();

        sw.ElapsedMilliseconds.Should()
            .BeGreaterThanOrEqualTo(
                350,
                "queue saturation wait (~400ms) must dominate the shorter pause (50ms) — if the if-guard were dropped the wait would collapse to ~50ms"
            );
    }
}
