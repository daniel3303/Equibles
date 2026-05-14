using Equibles.Integrations.Finra;
using Equibles.Integrations.Finra.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Equibles.UnitTests.Finra;

public class FinraClientTests
{
    [Fact]
    public void IsConfigured_ClientSecretSetButClientIdMissing_ReturnsFalse()
    {
        // FINRA's API uses OAuth2 client_credentials, where BOTH `ClientId` and
        // `ClientSecret` are required to obtain an access token. FinraClient.IsConfigured
        // gates the entire FINRA scraper via FinraScraperWorker.ValidateConfiguration —
        // a `false` here causes the worker to exit cleanly, while a `true` here lets
        // the worker proceed to OAuth and inevitably fail with 401.
        //
        // The risk this test pins: a refactor of the `&&` to `||` (or accidentally
        // checking ClientSecret twice, or only checking one of the two fields) would
        // let a misconfigured environment with ONE of the two credentials set slip past
        // ValidateConfiguration. Every scrape cycle would then burn the FINRA rate-limit
        // budget on rejected token requests, log them as errors, sleep, retry — a hot
        // loop that's invisible until someone notices stale FINRA data days later.
        //
        // The existing FinraScraperWorker test substitutes IFinraClient.IsConfigured
        // with a hand-written `false` — it would pass even if the production property's
        // logic were silently inverted. This test exercises the *real* property.
        //
        // ClientId="" + ClientSecret="real-secret-here" is the precise asymmetric input
        // that distinguishes `&&` (returns false, correct) from `||` (returns true, buggy)
        // AND from a copy-paste where both legs check the same field (returns true, buggy).
        var options = Options.Create(
            new FinraOptions { ClientId = "", ClientSecret = "real-secret-here" }
        );
        var sut = new FinraClient(new HttpClient(), NullLogger<FinraClient>.Instance, options);

        sut.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_BothClientIdAndClientSecretSet_ReturnsTrue()
    {
        // Sibling to both false-case pins above. The risk this catches is
        // asymmetric and unreachable from the existing pair: a regression
        // that hard-codes `IsConfigured => false` (defensive default during
        // a refactor, or copy-paste from a perpetually-disabled client)
        // PASSES both existing tests (`!IsNullOrEmpty("") && ...` → false
        // either way, with or without the property body) and only shows up
        // here.
        //
        // The pair (id-only → false, secret-only → false, both → true)
        // distinguishes a working `&&` conjunction from THREE possible
        // regressions:
        //   1. `||` swap (id-only would return true — caught by first sibling)
        //   2. duplicated leg `ClientSecret && ClientSecret` (id-only would
        //      return true — caught by first sibling)
        //   3. `=> false` constant return (both false-cases still false, but
        //      this true-case fails — caught ONLY here)
        //
        // Without this pin, an "always-false" regression silently disables
        // the FINRA scraper. Every scrape cycle exits via the worker's
        // ValidateConfiguration guard with no exception, no Warning log
        // from a misconfigured environment (the guard's whole point is to
        // exit silently). Short-volume + short-interest data freezes with
        // no operator-visible signal — the same failure mode the two
        // existing false-case pins were written to prevent, just via the
        // opposite-direction regression.
        var options = Options.Create(
            new FinraOptions { ClientId = "real-id-here", ClientSecret = "real-secret-here" }
        );
        var sut = new FinraClient(new HttpClient(), NullLogger<FinraClient>.Instance, options);

        sut.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void IsConfigured_ClientIdSetButClientSecretMissing_ReturnsFalse()
    {
        // Mirror to the existing ClientSecretSetButClientIdMissing test.
        // The sibling exercises the first leg of the `&&` (ClientId
        // missing → short-circuits to false before reading ClientSecret).
        // This pins the OPPOSITE asymmetry: ClientId set so the first
        // leg passes, forcing evaluation of the second leg, which must
        // also reject when ClientSecret is missing. Without this pin
        // a refactor that checks ClientId twice (or returns true after
        // the first leg passes) would silently let an environment with
        // only the public client id slip past ValidateConfiguration.
        var options = Options.Create(
            new FinraOptions { ClientId = "real-id-here", ClientSecret = "" }
        );
        var sut = new FinraClient(new HttpClient(), NullLogger<FinraClient>.Instance, options);

        sut.IsConfigured.Should().BeFalse();
    }
}
