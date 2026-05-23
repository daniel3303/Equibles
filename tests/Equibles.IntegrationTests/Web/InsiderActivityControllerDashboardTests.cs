using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class InsiderActivityControllerDashboardTests
{
    private readonly WebHostFixture _fixture;

    public InsiderActivityControllerDashboardTests(WebHostFixture fixture) => _fixture = fixture;

    // The insider trading dashboard (#1931) has a functional (Playwright) test
    // but zero integration coverage through WebApplicationFactory. This test
    // exercises routing → controller → repository queries → Razor view with
    // seeded buy and sell transactions, asserting the three board cards render.
    [Fact]
    public async Task Dashboard_WithSeededTransactions_RendersAllThreeBoards()
    {
        var stockId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "AAPL",
                    Name = "Apple Inc.",
                    Cik = "0000320193",
                }
            );
            db.Add(
                new InsiderOwner
                {
                    Id = ownerId,
                    OwnerCik = "0005000001",
                    Name = "Jane Doe",
                    IsOfficer = true,
                    OfficerTitle = "CEO",
                }
            );
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stockId,
                    InsiderOwnerId = ownerId,
                    TransactionDate = today.AddDays(-5),
                    FilingDate = today.AddDays(-3),
                    TransactionCode = TransactionCode.Purchase,
                    AcquiredDisposed = AcquiredDisposed.Acquired,
                    Shares = 10_000,
                    PricePerShare = 150.00m,
                    SharesOwnedAfter = 50_000,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0005000001-25-000001",
                }
            );
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = stockId,
                    InsiderOwnerId = ownerId,
                    TransactionDate = today.AddDays(-10),
                    FilingDate = today.AddDays(-8),
                    TransactionCode = TransactionCode.Sale,
                    AcquiredDisposed = AcquiredDisposed.Disposed,
                    Shares = 5_000,
                    PricePerShare = 145.00m,
                    SharesOwnedAfter = 40_000,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0005000001-25-000002",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/insider-trading/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("data-testid=\"insider-top-buys\"");
        html.Should().Contain("data-testid=\"insider-top-sells\"");
        html.Should().Contain("data-testid=\"insider-biggest\"");
        html.Should().Contain("Jane Doe");
        html.Should().Contain("AAPL");
    }
}
