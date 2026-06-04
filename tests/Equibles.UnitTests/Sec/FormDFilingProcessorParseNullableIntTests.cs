using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FormDFilingProcessorParseNullableIntTests
{
    // ParseNullableInt returns null only when the field is missing/invalid — a real
    // reported "0" must round-trip to 0, not null. Asserting "0" → 0 alongside "" →
    // null pins that discriminator; a guard that treated falsy/empty as null would
    // wrongly collapse a genuine zero count.
    [Fact]
    public void ParseNullableInt_ZeroIsPreservedAndEmptyIsNull()
    {
        FormDFilingProcessor.ParseNullableInt("0").Should().Be(0);
        FormDFilingProcessor.ParseNullableInt("").Should().BeNull();
    }
}
