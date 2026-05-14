using Equibles.Integrations.Fred;
using Equibles.Integrations.Fred.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.UnitTests.Fred;

public class FredClientTests
{
    [Fact]
    public void IsConfigured_EmptyApiKey_ReturnsFalse()
    {
        // FRED's API requires every request to carry `api_key={key}` in the query string —
        // missing or empty keys are rejected with HTTP 400 by api.stlouisfed.org. FredClient.IsConfigured
        // is the gate that FredScraperWorker.ValidateConfiguration consults: a `false` here
        // exits the worker cleanly with a "FRED__ApiKey not configured" warning, while a
        // `true` here lets the worker proceed into the rate-limited request loop where every
        // call will 400 and burn the operator's FRED rate-limit budget.
        //
        // The risk this test pins: a refactor that drops the IsNullOrEmpty check (returning
        // true on a non-null but empty key), or that negates the condition by mistake, would
        // let a misconfigured environment slip past ValidateConfiguration into a hot retry
        // loop. The existing FredScraperWorker test stubs IFredClient.IsConfigured directly
        // — it would pass even if the production property's logic were inverted. This test
        // exercises the real property against the realistic mis-config scenario (operator
        // set the env var but to an empty string instead of unsetting it).
        var options = Options.Create(new FredOptions { ApiKey = "" });
        var sut = new FredClient(new HttpClient(), NullLogger<FredClient>.Instance, options);

        sut.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_NonEmptyApiKey_ReturnsTrue()
    {
        // Sibling to the false-case pin above. The risk this pin catches is asymmetric
        // and unreachable from the empty-key sibling alone: a regression that hard-codes
        // `IsConfigured => false` (e.g. a defensive default added during a refactor, or
        // an accidental constant from a copy-paste) passes the empty-key test and only
        // shows up here. Without this pin, an "always-false" regression would silently
        // disable the entire FRED scraper in production — FredScraperWorker.ValidateConfiguration
        // would loop log "FRED__ApiKey not configured" forever, and economic-indicator
        // data would stop flowing without any CI signal.
        //
        // The pair (empty → false, non-empty → true) is what distinguishes a working
        // `!string.IsNullOrEmpty(ApiKey)` from BOTH inversion (`string.IsNullOrEmpty`,
        // caught by the false sibling) AND constant-return regressions (caught only
        // here). Pick a realistic API-key-shaped value rather than just "x" so a
        // future refactor that adds key-format validation (length, charset) won't
        // silently invalidate this pin without a clear failure mode.
        var options = Options.Create(
            new FredOptions { ApiKey = "abcdef0123456789abcdef0123456789" }
        );
        var sut = new FredClient(new HttpClient(), NullLogger<FredClient>.Instance, options);

        sut.IsConfigured.Should().BeTrue();
    }
}
