using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsDataSetClientTests {
    [Fact]
    public void GetDataSetFileNames_StartDateBeforeEarliestAvailable_ClampsToQ2_2013() {
        var result = HoldingsDataSetClient.GetDataSetFileNames(new DateTime(2010, 1, 1));

        result.Should().Contain("2013q2_form13f.zip");
        result.Should().NotContain("2013q1_form13f.zip");
    }
}
