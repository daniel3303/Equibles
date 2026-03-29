using Equibles.Web.Authentication;

namespace Equibles.Tests.Web;

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
    public void ConstantTimeEquals_NullAndEmpty_ReturnsTrue() {
        // Both null and empty hash to the same value because the implementation
        // coalesces null to "" before hashing
        EnvAuthHandler.ConstantTimeEquals(null!, "").Should().BeTrue();
    }
}
