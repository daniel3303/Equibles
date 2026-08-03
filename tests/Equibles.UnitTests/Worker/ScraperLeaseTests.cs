#nullable enable

using Equibles.Worker;

namespace Equibles.UnitTests.Worker;

// The lock key is the whole lease. Two worker instances only serialize on a lane if both
// compute the SAME key for the same WorkerName — in different processes, on different hosts,
// across restarts. That rules out `string.GetHashCode()`, which .NET randomizes per process:
// it is stable within one run, so a same-process "is it deterministic?" test passes while the
// two containers in production silently take different locks and never exclude each other.
//
// So these pin the key against hardcoded FNV-1a values computed independently of the code.
// A golden constant is the only assertion that catches BOTH a changed algorithm and a
// per-process one — comparing the implementation to itself, in any number of processes, is
// tautological and passes under both.
public class ScraperLeaseTests
{
    [Theory]
    [InlineData("Document processor", 6530301888989173848L)]
    [InlineData("Government contracts scraper", 2686007587183620975L)]
    [InlineData("", -3750763034362895579L)]
    public void ComputeLockKey_MatchesTheFnv1aGoldenValue(string workerName, long expected)
    {
        ScraperLease.ComputeLockKey(workerName).Should().Be(expected);
    }

    [Fact]
    public void ComputeLockKey_DifferentWorkerNames_DoNotShareALane()
    {
        // Distinct lanes must not collide onto one advisory lock, or one worker would block an
        // unrelated scraper for a whole cycle.
        ScraperLease
            .ComputeLockKey("Document processor")
            .Should()
            .NotBe(ScraperLease.ComputeLockKey("Government contracts scraper"));
    }

    [Fact]
    public void ComputeLockKey_NullWorkerName_Throws()
    {
        var act = () => ScraperLease.ComputeLockKey(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
