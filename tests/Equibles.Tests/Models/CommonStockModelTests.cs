using Equibles.CommonStocks.Data.Models;

namespace Equibles.Tests.Models;

public class CommonStockModelTests {
    [Fact]
    public void NewInstance_ShouldHaveNonEmptyGuid() {
        var stock = new CommonStock();

        stock.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void TwoInstances_ShouldHaveDifferentGuids() {
        var stock1 = new CommonStock();
        var stock2 = new CommonStock();

        stock1.Id.Should().NotBe(stock2.Id);
    }

    [Fact]
    public void SecondaryTickers_ShouldDefaultToEmptyList() {
        var stock = new CommonStock();

        stock.SecondaryTickers.Should().NotBeNull();
        stock.SecondaryTickers.Should().BeEmpty();
    }

    [Fact]
    public void SecondaryTickers_CanBeSetAndRetrieved() {
        var stock = new CommonStock();
        var tickers = new List<string> { "AAPL", "GOOG" };

        stock.SecondaryTickers = tickers;

        stock.SecondaryTickers.Should().BeEquivalentTo(tickers);
    }

    [Fact]
    public void StringProperties_ShouldDefaultToNull() {
        var stock = new CommonStock();

        stock.Ticker.Should().BeNull();
        stock.Name.Should().BeNull();
        stock.Description.Should().BeNull();
        stock.Cik.Should().BeNull();
        stock.Website.Should().BeNull();
        stock.Cusip.Should().BeNull();
    }

    [Fact]
    public void MarketCapitalization_ShouldDefaultToZero() {
        var stock = new CommonStock();

        stock.MarketCapitalization.Should().Be(0);
    }

    [Fact]
    public void SharesOutStanding_ShouldDefaultToZero() {
        var stock = new CommonStock();

        stock.SharesOutStanding.Should().Be(0);
    }

    [Fact]
    public void IndustryId_ShouldDefaultToNull() {
        var stock = new CommonStock();

        stock.IndustryId.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSetAndReadBack() {
        var id = Guid.NewGuid();
        var industryId = Guid.NewGuid();

        var stock = new CommonStock {
            Id = id,
            Ticker = "MSFT",
            Name = "Microsoft Corporation",
            Description = "Technology company",
            Cik = "0000789019",
            Website = "https://microsoft.com",
            MarketCapitalization = 2_500_000_000_000.0,
            SharesOutStanding = 7_500_000_000,
            SecondaryTickers = ["MSFT.L"],
            Cusip = "594918104",
            IndustryId = industryId
        };

        stock.Id.Should().Be(id);
        stock.Ticker.Should().Be("MSFT");
        stock.Name.Should().Be("Microsoft Corporation");
        stock.Description.Should().Be("Technology company");
        stock.Cik.Should().Be("0000789019");
        stock.Website.Should().Be("https://microsoft.com");
        stock.MarketCapitalization.Should().Be(2_500_000_000_000.0);
        stock.SharesOutStanding.Should().Be(7_500_000_000);
        stock.SecondaryTickers.Should().ContainSingle().Which.Should().Be("MSFT.L");
        stock.Cusip.Should().Be("594918104");
        stock.IndustryId.Should().Be(industryId);
    }
}
