using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FormDFilingProcessorParseLongGroupedTests
{
    // Form D dollar figures can arrive with thousands separators; ParseLong uses
    // NumberStyles.Any under InvariantCulture, so "5,000,000" must read as 5000000
    // (comma = grouping) regardless of host locale. A German-culture parse without
    // the invariant pin would treat "," as a decimal point and misread the figure.
    [Fact]
    public void ParseLong_ThousandsGroupedFigure_ParsesGroupingInvariantly()
    {
        FormDFilingProcessor.ParseLong("5,000,000").Should().Be(5_000_000L);
    }
}
