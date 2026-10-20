using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class Schedule13DGPositionResolverTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("N/A", true)]
    [InlineData("000000000", true)]
    [InlineData("67066G105", true)]
    [InlineData("67066G104", false)]
    [InlineData(" q4789u120 ", false)]
    public void NeedsSubjectCompany_OnlyWithoutAUsableCusip(string filed, bool expected) =>
        Schedule13DGPositionResolver.NeedsSubjectCompany(filed).Should().Be(expected);

    [Theory]
    // 9,984,820 of 118,000,000 is 8.46%.
    [InlineData(9_984_820L, "8.4", 118_000_000L, true)]
    [InlineData(9_984_820L, "8.4", 100_000_000L, true)]
    [InlineData(9_984_820L, "8.4", 90_000_000L, false)]
    [InlineData(9_984_820L, "8.4", 160_000_000L, false)]
    // Ordinary shares counted against a depositary share standing for ten of them.
    [InlineData(26_017_582L, "8.2", 36_224_016L, false)]
    // A zero stake (an exit) cannot tell one class from another.
    [InlineData(0L, "0", 118_000_000L, false)]
    [InlineData(5L, "0", 118_000_000L, false)]
    [InlineData(9_984_820L, null, 118_000_000L, false)]
    [InlineData(9_984_820L, "8.4", 0L, false)]
    [InlineData(null, "8.4", 118_000_000L, false)]
    public void AgreesWithSharesOutstanding_WithinTheToleranceOfTheFiledPercent(
        long? amount,
        string percent,
        long sharesOutstanding,
        bool expected
    ) =>
        Schedule13DGPositionResolver
            .AgreesWithSharesOutstanding(
                amount,
                percent == null
                    ? null
                    : decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture),
                sharesOutstanding
            )
            .Should()
            .Be(expected);
}
