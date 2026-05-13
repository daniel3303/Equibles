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
    public void ConstantTimeEquals_NullAndEmpty_ReturnsTrue() {
        // Both null and empty hash to the same value because the implementation
        // coalesces null to "" before hashing
        EnvAuthHandler.ConstantTimeEquals(null!, "").Should().BeTrue();
    }
}
