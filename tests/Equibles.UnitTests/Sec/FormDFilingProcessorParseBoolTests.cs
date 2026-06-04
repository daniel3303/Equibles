using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FormDFilingProcessorParseBoolTests
{
    // Form D boolean fields carry XSD "true"/"false"; ParseBool must return the parsed
    // value, not just "non-empty → true". Asserting "false" → false (alongside "true" →
    // true) pins the discriminator — a regression to a presence check would read the
    // explicit "false" as true.
    [Fact]
    public void ParseBool_TrueIsTrueAndFalseIsFalse()
    {
        FormDFilingProcessor.ParseBool("true").Should().BeTrue();
        FormDFilingProcessor.ParseBool("false").Should().BeFalse();
    }
}
