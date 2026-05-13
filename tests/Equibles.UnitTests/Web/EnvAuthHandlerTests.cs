using System.Security.Claims;
using System.Text.Encodings.Web;
using Equibles.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Web;

public class EnvAuthHandlerTests {
    [Fact]
    public void SchemeName_IsEnvAuth() {
        EnvAuthHandler.SchemeName.Should().Be("EnvAuth");
    }

    [Fact]
    public void AnonymousUsername_IsAnonymous() {
        EnvAuthHandler.AnonymousUsername.Should().Be("anonymous");
    }

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString() {
        var token = EnvAuthHandler.GenerateToken("user", "secret");

        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GenerateToken_SameInput_ProducesConsistentOutput() {
        var token1 = EnvAuthHandler.GenerateToken("user", "secret");
        var token2 = EnvAuthHandler.GenerateToken("user", "secret");

        token1.Should().Be(token2);
    }

    [Fact]
    public void GenerateToken_DifferentUsername_ProducesDifferentToken() {
        var token1 = EnvAuthHandler.GenerateToken("alice", "secret");
        var token2 = EnvAuthHandler.GenerateToken("bob", "secret");

        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateToken_DifferentSecret_ProducesDifferentToken() {
        var token1 = EnvAuthHandler.GenerateToken("user", "secret1");
        var token2 = EnvAuthHandler.GenerateToken("user", "secret2");

        token1.Should().NotBe(token2);
    }

    [Fact]
    public void GenerateToken_OutputIsValidBase64() {
        var token = EnvAuthHandler.GenerateToken("user", "secret");

        var act = () => Convert.FromBase64String(token);
        act.Should().NotThrow();
    }

    [Fact]
    public void ConstantTimeEquals_EqualStrings_ReturnsTrue() {
        EnvAuthHandler.ConstantTimeEquals("hello", "hello").Should().BeTrue();
    }

    [Fact]
    public void ConstantTimeEquals_DifferentStrings_ReturnsFalse() {
        EnvAuthHandler.ConstantTimeEquals("hello", "world").Should().BeFalse();
    }

    [Fact]
    public void ConstantTimeEquals_BothNull_ReturnsTrue() {
        EnvAuthHandler.ConstantTimeEquals(null!, null!).Should().BeTrue();
    }

    [Fact]
    public void ConstantTimeEquals_OneNull_ReturnsFalse() {
        EnvAuthHandler.ConstantTimeEquals(null!, "hello").Should().BeFalse();
    }

    [Fact]
    public void ConstantTimeEquals_OtherNull_ReturnsFalse() {
        EnvAuthHandler.ConstantTimeEquals("hello", null!).Should().BeFalse();
    }

    [Fact]
    public void ConstantTimeEquals_BothEmpty_ReturnsTrue() {
        EnvAuthHandler.ConstantTimeEquals("", "").Should().BeTrue();
    }

    [Fact]
    public void ConstantTimeEquals_EmptyAndNonEmpty_ReturnsFalse() {
        EnvAuthHandler.ConstantTimeEquals("", "hello").Should().BeFalse();
    }

    [Fact]
    public async Task HandleAuthenticateAsync_AuthDisabled_SucceedsAsAnonymousUser() {
        // EnvAuthHandler.HandleAuthenticateAsync has four reachable paths:
        //   1. !IsEnabled              → Success(anonymous)
        //   2. IsEnabled, no cookie    → Fail("Not authenticated")
        //   3. IsEnabled, bad cookie   → Fail("Invalid session")
        //   4. IsEnabled, good cookie  → Success(real user)
        // Every existing pin in this file targets the static helpers (GenerateToken,
        // ConstantTimeEquals) — the four runtime branches of HandleAuthenticateAsync
        // are entirely unpinned. This pin covers branch 1, which is load-bearing in
        // two practical scenarios:
        //   • Local dev / CI runs (AUTH_ENABLED=false), where every endpoint sees an
        //     anonymous principal with ClaimTypes.Name == "anonymous". The downstream
        //     `Url.IsLocalUrl`, return-URL handling, and BaseController logging all
        //     depend on Identity.Name being populated — a regression that returns
        //     `AuthenticateResult.NoResult()` (the "no claims" default for skipped
        //     handlers) or `Fail` would 401 every dev request without warning.
        //   • Public/preview deployments where the operator deliberately ships
        //     read-only without auth — same anonymous contract.
        //
        // A refactor that flipped the `!IsEnabled` branch to the IsEnabled body
        // (or that returned NoResult instead of Success(anonymous)) would still
        // pass every static-helper sibling pin and only surface as an empty-
        // principal failure on the first runtime request. Pin the contract: when
        // IsEnabled=false, the result MUST be Succeeded with Name="anonymous".
        // AuthSettings.IsEnabled is computed: `!IsNullOrEmpty(Username) && !IsNullOrEmpty(Password)`.
        // Leaving both unset (the default) yields IsEnabled=false — the dev/CI scenario.
        var authSettings = Options.Create(new AuthSettings());
        var schemeOptions = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var sut = new EnvAuthHandler(schemeOptions, NullLoggerFactory.Instance, UrlEncoder.Default, authSettings);

        var scheme = new AuthenticationScheme(EnvAuthHandler.SchemeName, EnvAuthHandler.SchemeName, typeof(EnvAuthHandler));
        var httpContext = new DefaultHttpContext();
        await sut.InitializeAsync(scheme, httpContext);
        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.FindFirstValue(ClaimTypes.Name).Should().Be(EnvAuthHandler.AnonymousUsername);
    }

    [Fact]
    public async Task HandleAuthenticateAsync_AuthEnabledWithNoCookie_FailsWithNotAuthenticated() {
        // Sibling pin to HandleAuthenticateAsync_AuthDisabled_SucceedsAsAnonymousUser.
        // That pin covers branch 1 (!IsEnabled → Success). This covers branch 2
        // (IsEnabled, no cookie → Fail("Not authenticated")).
        //
        // The risk this pins is asymmetric: a refactor that inverts the
        // `string.IsNullOrEmpty(cookie)` check (returning Success on missing cookie
        // instead of Fail) would let every unauthenticated request through under a
        // production deployment with AUTH_ENABLED=true. The failure mode is catastrophic
        // — every protected endpoint is suddenly public-accessible. CI without this
        // pin wouldn't catch the inversion since the existing anonymous-mode test only
        // exercises the !IsEnabled branch and the static-helper tests don't go through
        // HandleAuthenticateAsync at all.
        //
        // The Fail reason "Not authenticated" is asserted via FailureMessage so a
        // refactor that swapped messages (e.g. accidentally pasted "Invalid session"
        // from the next branch) is caught — that mismatch would mis-route operator
        // log queries that filter by failure reason ("session expired" alert vs
        // "missing cookie" alert).
        //
        // Construction: enable auth by setting both Username and Password (production
        // pattern — AuthSettings.IsEnabled is the computed AND of both being non-empty).
        // Use a fresh DefaultHttpContext with no cookies attached. AuthenticateAsync
        // walks the cookie collection, finds the EnvAuth scheme cookie missing,
        // returns Fail.
        var authSettings = Options.Create(new AuthSettings {
            Username = "admin",
            Password = "secret123",
        });
        var schemeOptions = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var sut = new EnvAuthHandler(schemeOptions, NullLoggerFactory.Instance, UrlEncoder.Default, authSettings);

        var scheme = new AuthenticationScheme(EnvAuthHandler.SchemeName, EnvAuthHandler.SchemeName, typeof(EnvAuthHandler));
        var httpContext = new DefaultHttpContext();
        await sut.InitializeAsync(scheme, httpContext);
        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Not authenticated");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_AuthEnabledWithInvalidCookie_FailsWithInvalidSession() {
        // Third sibling in the HandleAuthenticateAsync branch family. Covers branch 3:
        // IsEnabled, cookie present but does NOT match the expected token → Fail("Invalid session").
        //
        // The Fail message distinction between "Not authenticated" (no cookie, branch 2)
        // and "Invalid session" (wrong cookie, this branch) is operator-visible: log
        // queries that filter by failure reason distinguish "user never logged in" from
        // "user's session expired or the secret rotated." Operator runbooks branch on
        // this distinction — pin both messages so a swap between branches is caught.
        //
        // The risk this asymmetric pin catches: a refactor that drops the
        // `!ConstantTimeEquals(cookie, expectedToken)` check (or inverts it) would let
        // any non-empty cookie value succeed as authenticated, bypassing the session-
        // token check entirely. The branch-2 pin can't catch this — it has no cookie
        // at all, so the cookie-check branch never fires. Without this pin, an attacker
        // could send `Cookie: EnvAuth=anything` and authenticate as the configured user.
        //
        // Construction: enable auth (Username + Password), attach a cookie with a
        // bogus value. The header form is `Cookie: EnvAuth=invalid-token-value`.
        // DefaultHttpContext.Request.Cookies parses this header into the collection
        // the handler reads via Request.Cookies[SchemeName].
        var authSettings = Options.Create(new AuthSettings {
            Username = "admin",
            Password = "secret123",
        });
        var schemeOptions = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var sut = new EnvAuthHandler(schemeOptions, NullLoggerFactory.Instance, UrlEncoder.Default, authSettings);

        var scheme = new AuthenticationScheme(EnvAuthHandler.SchemeName, EnvAuthHandler.SchemeName, typeof(EnvAuthHandler));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cookie"] = $"{EnvAuthHandler.SchemeName}=tampered-session-token";
        await sut.InitializeAsync(scheme, httpContext);
        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
        result.Failure!.Message.Should().Be("Invalid session");
    }

    [Fact]
    public async Task HandleAuthenticateAsync_AuthEnabledWithValidCookie_SucceedsAsConfiguredUser() {
        // Final sibling in the HandleAuthenticateAsync branch family. Covers branch 4:
        // IsEnabled, cookie matches expected token → Success(principal with Username).
        //
        // The three previous siblings (!IsEnabled, no cookie, bogus cookie) all cover
        // the rejection/anonymous paths. None proves the HAPPY-PATH success — a regression
        // that returned Fail or NoResult on the matching-cookie branch would compile
        // cleanly, pass every existing sibling pin, and silently lock every legitimate
        // operator out of the production deployment. The failure mode is invisible to
        // the rejection-only siblings: they only assert that bad input fails, never that
        // good input succeeds.
        //
        // The principal's Identity.Name must equal the CONFIGURED username (not
        // AnonymousUsername — that's the !IsEnabled path). A refactor that copy-pasted
        // the anonymous-claim construction into the authenticated branch (or that
        // forgot to use _authSettings.Username and fell back to a constant) would set
        // the wrong principal Name, breaking downstream audit logging and BaseController
        // operator-attribution. Pin Name == "admin" (the configured value) so that swap
        // is caught at test time.
        //
        // Construction: compute the expected token via the public GenerateToken helper
        // (so this test stays valid if the hashing scheme changes — the existing
        // GenerateToken pins lock the algorithm). Attach the computed token as the
        // EnvAuth cookie value. AuthenticateAsync hashes the cookie value, hashes the
        // expected (Username, SessionSecret) tuple, ConstantTimeEquals returns true,
        // Success(principal) is returned with Identity.Name == "admin".
        const string username = "admin";
        const string password = "secret123";
        const string sessionSecret = "test-session-secret";

        var authSettings = Options.Create(new AuthSettings {
            Username = username,
            Password = password,
            SessionSecret = sessionSecret,
        });
        var schemeOptions = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var sut = new EnvAuthHandler(schemeOptions, NullLoggerFactory.Instance, UrlEncoder.Default, authSettings);

        var validToken = EnvAuthHandler.GenerateToken(username, sessionSecret);
        var scheme = new AuthenticationScheme(EnvAuthHandler.SchemeName, EnvAuthHandler.SchemeName, typeof(EnvAuthHandler));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cookie"] = $"{EnvAuthHandler.SchemeName}={validToken}";
        await sut.InitializeAsync(scheme, httpContext);
        var result = await sut.AuthenticateAsync();

        result.Succeeded.Should().BeTrue();
        result.Principal.Should().NotBeNull();
        result.Principal!.FindFirstValue(ClaimTypes.Name).Should().Be(username);
    }

    [Fact]
    public async Task HandleChallengeAsync_UrlWithQueryString_RedirectsToLoginWithEscapedReturnUrl() {
        // EnvAuthHandler.HandleChallengeAsync builds a 302 redirect to /Auth/Login
        // carrying the requested URL (path + query) as a ReturnUrl query parameter.
        // The implementation is:
        //   var returnUrl = Request.Path + Request.QueryString;
        //   Response.Redirect($"/Auth/Login?ReturnUrl={Uri.EscapeDataString(returnUrl)}");
        //
        // The Uri.EscapeDataString call is the security boundary. Without it, a
        // requested URL like `/Reports?id=1&admin=true` would produce a redirect
        // location of `/Auth/Login?ReturnUrl=/Reports?id=1&admin=true` — the
        // `&admin=true` from the original query string would now be parsed BY
        // /Auth/Login as a top-level query parameter rather than as part of
        // ReturnUrl. An attacker could craft a URL whose query string injects
        // parameters /Auth/Login interprets (think `&fallback=https://evil.com`),
        // breaking the open-redirect mitigations in BaseController.GetReturnUrl
        // (which validates ReturnUrl via Url.IsLocalUrl AFTER the round trip).
        //
        // Every existing pin in this file targets either the static helpers or the
        // four HandleAuthenticateAsync branches. The HandleChallengeAsync codepath
        // is entirely unpinned — a refactor that drops the `Uri.EscapeDataString`
        // call (e.g. someone "simplifying" the format string) would compile cleanly,
        // pass every existing test, and silently turn the redirect into an
        // open-redirect-by-query-pollution vector on every protected endpoint.
        //
        // Pin both the redirect target prefix (`/Auth/Login?ReturnUrl=`) AND the
        // escaped form of the original path+query (`%2FFoo%3Fbar%3Dbaz`). The
        // escaped-form assertion is what distinguishes a working
        // Uri.EscapeDataString from a refactor that dropped it — without the
        // escape, the location would contain literal `/Foo?bar=baz`.
        var authSettings = Options.Create(new AuthSettings());
        var schemeOptions = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        schemeOptions.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());
        var sut = new EnvAuthHandler(schemeOptions, NullLoggerFactory.Instance, UrlEncoder.Default, authSettings);

        var scheme = new AuthenticationScheme(EnvAuthHandler.SchemeName, EnvAuthHandler.SchemeName, typeof(EnvAuthHandler));
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/Foo";
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString("?bar=baz");
        await sut.InitializeAsync(scheme, httpContext);
        await sut.ChallengeAsync(new AuthenticationProperties());

        httpContext.Response.StatusCode.Should().Be(302);
        httpContext.Response.Headers.Location.ToString()
            .Should().Be("/Auth/Login?ReturnUrl=%2FFoo%3Fbar%3Dbaz");
    }

    [Fact]
    public void ConstantTimeEquals_NullAndEmpty_ReturnsTrue() {
        // Both null and empty hash to the same value because the implementation
        // coalesces null to "" before hashing
        EnvAuthHandler.ConstantTimeEquals(null!, "").Should().BeTrue();
    }
}
