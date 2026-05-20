using Equibles.Sec.FinancialFacts.Mcp.Helpers;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins the default-arm contract of FactArgs.TryParsePeriod: per the .NET
/// Try-pattern convention, any unrecognized fiscal-period string must return
/// false rather than silently succeed with a default SecFiscalPeriod.
/// Without this pin, a regression that "simplified" the switch into an
/// always-FullYear fallback would compile cleanly and silently misclassify
/// every typo'd period (e.g. "Q5", "first quarter") as the annual figure.
/// </summary>
public class FactArgsTryParsePeriodDefaultTests
{
    [Fact]
    public void TryParsePeriod_UnrecognizedValue_ReturnsFalse()
    {
        var success = FactArgs.TryParsePeriod("Q5", out _);

        success.Should().BeFalse();
    }
}
