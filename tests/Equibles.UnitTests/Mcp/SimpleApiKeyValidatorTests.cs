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
}
