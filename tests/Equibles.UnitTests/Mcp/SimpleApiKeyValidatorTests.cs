using Equibles.Mcp.Server;
using Microsoft.Extensions.Configuration;

namespace Equibles.UnitTests.Mcp;

public class SimpleApiKeyValidatorTests {
    [Fact]
    public async Task IsValid_EnabledWithMismatchedKey_ReturnsFalse() {
        // The validator hashes the configured key on construction and compares
        // it to the hashed candidate via FixedTimeEquals. Pin the rejection
        // path so a regression that bypasses the comparison (or swaps the
        // equality direction) can't silently accept arbitrary tokens.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["McpApiKey"] = "correct-secret" })
            .Build();
        var sut = new SimpleApiKeyValidator(config);

        var result = await sut.IsValid("wrong-secret");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsValid_EnabledWithMatchingKey_ReturnsTrue() {
        // Sibling pins exist for the two rejection paths: mismatched key → false,
        // and disabled mode (no key configured) → true. The remaining branch — the
        // actual success path, where IsEnabled is true AND the supplied key matches
        // the configured one — is the load-bearing one for production: every real
        // MCP client request hits this branch. Pin it explicitly so a regression
        // that breaks the equality comparison (e.g. a refactor that swaps the
        // argument order of FixedTimeEquals to compare against an uninitialized
        // buffer, or that accidentally hashes the candidate twice while leaving
        // _configuredKeyHash single-hashed) is caught at test time.
        //
        // The mismatched-key sibling alone can't prove the success path works:
        // both "comparison always returns false" and "comparison works correctly"
        // produce the same false result on a mismatched input. Only an explicit
        // match-success assertion distinguishes the two failure modes — and the
        // production consequence (every valid MCP client being locked out) is
        // exactly the kind of silent regression CI must catch.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["McpApiKey"] = "correct-secret" })
            .Build();
        var sut = new SimpleApiKeyValidator(config);

        sut.IsEnabled.Should().BeTrue();
        (await sut.IsValid("correct-secret")).Should().BeTrue();
    }

    [Fact]
    public async Task IsValid_DisabledModeWithMissingApiKeyConfig_AcceptsAnyToken() {
        // When McpApiKey is unset (typical for local dev / docker-compose
        // without auth), IsEnabled is false and IsValid must short-circuit to
        // true for ANY input — including empty strings — so the MCP server
        // accepts unauthenticated clients in that mode. Pin the pass-through
        // so a refactor that flips the default (e.g. fail-closed when no key
        // configured) surfaces immediately rather than silently locking every
        // local developer out of the MCP endpoint.
        var config = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
        var sut = new SimpleApiKeyValidator(config);

        sut.IsEnabled.Should().BeFalse();
        (await sut.IsValid("any-random-token")).Should().BeTrue();
        (await sut.IsValid("")).Should().BeTrue();
    }
}
