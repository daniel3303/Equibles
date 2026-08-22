using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class CongressionalTradeImportLedgerTests
{
    [Fact]
    public void SelectNextYear_PicksNewestMissingArchiveYear()
    {
        var year = CongressionalTradeImportLedger.SelectNextYear([2019, 2017], 2012, 2019);

        year.Should().Be(2018);
    }

    [Fact]
    public void SelectNextYear_AllYearsComplete_ReturnsNull()
    {
        var year = CongressionalTradeImportLedger.SelectNextYear([2012, 2013], 2012, 2013);

        year.Should().BeNull();
    }

    [Fact]
    public void SelectNextYear_InvalidRange_ReturnsNull()
    {
        var year = CongressionalTradeImportLedger.SelectNextYear([], 2012, 2011);

        year.Should().BeNull();
    }
}
