using Equibles.Holdings.HostedService.Services.ValueNormalizers;

namespace Equibles.Tests.Holdings;

public class PassthroughValueNormalizerTests {
    [Fact]
    public void Normalize_Zero_ReturnsZero() {
        PassthroughValueNormalizer.Instance.Normalize(0).Should().Be(0);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(1_000_000)]
    public void Normalize_PositiveValue_ReturnsSameValue(long value) {
        PassthroughValueNormalizer.Instance.Normalize(value).Should().Be(value);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-999_999)]
    public void Normalize_NegativeValue_ReturnsSameValue(long value) {
        PassthroughValueNormalizer.Instance.Normalize(value).Should().Be(value);
    }

    [Fact]
    public void Normalize_MaxValue_ReturnsSameValue() {
        PassthroughValueNormalizer.Instance.Normalize(long.MaxValue).Should().Be(long.MaxValue);
    }

    [Fact]
    public void Instance_IsSingleton() {
        PassthroughValueNormalizer.Instance.Should().BeSameAs(PassthroughValueNormalizer.Instance);
    }
}

public class ThousandsValueNormalizerTests {
    [Fact]
    public void Normalize_Zero_ReturnsZero() {
        ThousandsValueNormalizer.Instance.Normalize(0).Should().Be(0);
    }

    [Theory]
    [InlineData(1, 1_000)]
    [InlineData(500, 500_000)]
    public void Normalize_PositiveValue_ReturnsValueTimesThousand(long input, long expected) {
        ThousandsValueNormalizer.Instance.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_NegativeValue_ReturnsValueTimesThousand() {
        ThousandsValueNormalizer.Instance.Normalize(-1).Should().Be(-1_000);
    }

    [Fact]
    public void Normalize_MaxValue_ThrowsOverflowException() {
        var act = () => ThousandsValueNormalizer.Instance.Normalize(long.MaxValue);
        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void Instance_IsSingleton() {
        ThousandsValueNormalizer.Instance.Should().BeSameAs(ThousandsValueNormalizer.Instance);
    }
}
