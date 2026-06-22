using System.Reflection;
using Equibles.Integrations.GovernmentContracts;

namespace Equibles.UnitTests.GovernmentContracts;

public class UsaSpendingClientExponentialBackoffTests
{
    // Contract (method name + shared RetryBackoff): exponential backoff between
    // USAspending retries, f(n) = 2^(n+1) seconds — f(0)=2s, f(1)=4s, f(2)=8s.
    // This forwarder is private static and otherwise untested.
    //
    // The risk this pin uniquely catches: a "simplification" that stops delegating
    // to RetryBackoff.Exponential and inlines a drifted formula. Dropping the `+1`
    // (`Math.Pow(2, attempt)`) compiles and still grows exponentially but HALVES
    // every interval; USAspending answers an over-eager client with HTTP 429, so a
    // halved gap turns a transient throttle into a hard ban that silently starves
    // the federal-contracts ingest. Asserting exactly 8s at attempt=2 distinguishes
    // the correct 2^(attempt+1) from the halving (4s), index-swap (4s), and
    // doubled-shift (16s) regressions.
    //
    // Reflection-invoke since the method is private static.
    [Fact]
    public void ExponentialBackoff_AttemptTwo_Returns8Seconds()
    {
        var method = typeof(UsaSpendingClient).GetMethod(
            "ExponentialBackoff",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        var result = (TimeSpan)method!.Invoke(null, [2]);

        result.Should().Be(TimeSpan.FromSeconds(8));
    }
}
