using Equibles.Sec.HostedService.Configuration;

namespace Equibles.UnitTests.Sec;

public class DocumentNormalizationBackfillOptionsTests
{
    [Fact]
    public void Defaults_AreDisabledAndLeaveHeadroomForLiveIngestion()
    {
        var options = new DocumentNormalizationBackfillOptions();

        options.Enabled.Should().BeFalse();
        options.IncludeAllDocumentTypes.Should().BeFalse();
        options.BatchSize.Should().Be(16);
        options.DrainIntervalSeconds.Should().Be(60);
        options.PriorityAccessions.Should().BeEmpty();
    }
}
