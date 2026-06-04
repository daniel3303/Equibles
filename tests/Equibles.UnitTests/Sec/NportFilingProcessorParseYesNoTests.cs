using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class NportFilingProcessorParseYesNoTests
{
    // N-PORT boolean fields carry "Y"/"N"; ParseYesNo must map only the affirmative
    // to true. Asserting "N" → false (alongside "Y" → true) pins the discriminator —
    // a regression to a non-empty/truthy check would wrongly read "N" as true. Nport's
    // own ParseYesNo was unexercised (NCen has its own separate copy).
    [Fact]
    public void ParseYesNo_AffirmativeIsTrueAndNegativeIsFalse()
    {
        NportFilingProcessor.ParseYesNo("Y").Should().BeTrue();
        NportFilingProcessor.ParseYesNo("N").Should().BeFalse();
    }
}
