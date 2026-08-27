using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.BusinessLogic;
using FluentAssertions;
using Xunit;

namespace Equibles.UnitTests.Finra;

public class FinraTickerScopeTests
{
    private static readonly CommonStock Stock = new()
    {
        Ticker = "AAXJ",
        Name = "iShares Trust",
        SecondaryTickers = ["SOXX"],
    };

    [Fact]
    public void SecondaryListingUnavailable_PrimaryTicker_IsSupported()
    {
        FinraTickerScope.SecondaryListingUnavailable(Stock, "AAXJ", "short-volume")
            .Should()
            .BeNull();
    }

    [Fact]
    public void SecondaryListingUnavailable_SecondaryTicker_RefusesPrimaryRows()
    {
        var result = FinraTickerScope.SecondaryListingUnavailable(
            Stock,
            "SOXX",
            "short-volume"
        );

        result.Should().Contain("No exact short-volume series is available for SOXX");
        result.Should().Contain("AAXJ's FINRA rows are not substituted");
    }

    [Fact]
    public void SecondaryListingUnavailable_CompanyName_IsNotTreatedAsAListing()
    {
        FinraTickerScope.SecondaryListingUnavailable(Stock, "iShares Trust", "short-volume")
            .Should()
            .BeNull();
    }
}
