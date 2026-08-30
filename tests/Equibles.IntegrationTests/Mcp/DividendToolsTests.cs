using System.Globalization;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Yahoo.Mcp.Tools;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class DividendToolsTests : ParadeDbMcpTestBase
{
    public DividendToolsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private DividendTools Sut() =>
        new(
            new CashDividendRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<DividendTools>()
        );

    private async Task<CommonStock> SeedStock(
        string ticker = "AAPL",
        string name = "Apple Inc.",
        params string[] secondaryTickers
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = name,
            Cik = Random.Shared.NextInt64(1_000_000_000L, 9_999_999_999L).ToString(),
            SecondaryTickers = secondaryTickers.ToList(),
        };
        DbContext.Add(stock);
        await DbContext.SaveChangesAsync();
        return stock;
    }

    private async Task SeedDividend(
        CommonStock stock,
        DateOnly exDate,
        decimal amount,
        CashDividendSource source = CashDividendSource.Yahoo
    )
    {
        DbContext.Add(
            new CashDividend
            {
                CommonStockId = stock.Id,
                CommonStock = stock,
                ExDate = exDate,
                AmountPerShare = amount,
                Source = source,
            }
        );
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task GetDividendHistory_ReturnsNewestFirstWithSource()
    {
        var stock = await SeedStock();
        await SeedDividend(stock, new DateOnly(2025, 2, 10), 0.25m);
        await SeedDividend(stock, new DateOnly(2025, 5, 12), 0.26m, CashDividendSource.External);

        var result = await Sut().GetDividendHistory("AAPL");

        result.Should().Contain("Declared cash dividends for Apple Inc. (AAPL), newest first:");
        result.IndexOf("2025-05-12").Should().BeLessThan(result.IndexOf("2025-02-10"));
        result.Should().Contain("$0.26 | External");
    }

    [Fact]
    public async Task GetDividendHistory_FiltersByExDateAndPagesWithOffset()
    {
        var stock = await SeedStock();
        await SeedDividend(stock, new DateOnly(2025, 2, 10), 0.25m);
        await SeedDividend(stock, new DateOnly(2025, 5, 12), 0.26m);
        await SeedDividend(stock, new DateOnly(2025, 8, 11), 0.27m);

        var filtered = await Sut()
            .GetDividendHistory(
                "AAPL",
                startDate: new DateTime(2025, 5, 1),
                endDate: new DateTime(2025, 8, 1)
            );
        var secondPage = await Sut().GetDividendHistory("AAPL", maxResults: 1, offset: 1);

        filtered.Should().Contain("2025-05-12");
        filtered.Should().NotContain("2025-02-10");
        filtered.Should().NotContain("2025-08-11");
        secondPage.Should().Contain("2025-05-12");
        secondPage.Should().Contain("pass offset=2 to continue");
    }

    [Fact]
    public async Task GetDividendHistory_SecondaryTicker_IsRejected()
    {
        await SeedStock("BRK-B", "Berkshire Hathaway Inc.", "BRK-A");

        var result = await Sut().GetDividendHistory("BRK.A");

        result.Should().Contain("available only for the current primary ticker BRK-B");
        result.Should().Contain("BRK-A is a separate listing");
    }

    [Fact]
    public async Task GetDividendHistory_InvertedDateRange_ReturnsCorrectiveError()
    {
        var result = await Sut()
            .GetDividendHistory(
                "AAPL",
                startDate: new DateTime(2025, 8, 1),
                endDate: new DateTime(2025, 5, 1)
            );

        result.Should().Contain("startDate 2025-08-01 is after endDate 2025-05-01");
    }

    [Fact]
    public async Task GetDividendHistory_UnderNonGregorianCulture_RendersIsoDate()
    {
        var stock = await SeedStock();
        await SeedDividend(stock, new DateOnly(2025, 5, 12), 0.26m);

        var previous = CultureInfo.CurrentCulture;
        string result;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            result = await Sut().GetDividendHistory("AAPL");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        result.Should().Contain("2025-05-12 | $0.26");
    }

    [Fact]
    public async Task GetDividendHistory_EscapesCompanyNameInHeading()
    {
        var stock = await SeedStock(name: "Pipe | Corp\\Line\nTwo");
        await SeedDividend(stock, new DateOnly(2025, 5, 12), 0.26m);

        var result = await Sut().GetDividendHistory("AAPL");

        result.Should().Contain(@"Pipe \| Corp\\Line Two (AAPL)");
        result.Should().NotContain("Corp\\Line\nTwo");
    }
}
