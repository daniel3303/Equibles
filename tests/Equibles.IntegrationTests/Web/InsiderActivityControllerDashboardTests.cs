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

    // A derivative's PricePerShare is the instrument's own price, so Shares × Price
    // is not a dollar value — option/warrant/convertible rows with multi-million
    // "prices" would otherwise dominate the value sort. They must be excluded both
    // by authoritative kind and by the title fallback for not-yet-reclassified rows.
    [Fact]
    public async Task Dashboard_ExcludesDerivativesFromValueBoards()
    {
        var shareStockId = Guid.NewGuid();
        var derivKindStockId = Guid.NewGuid();
        var derivTitleStockId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = shareStockId,
                    Ticker = "SHRE",
                    Name = "Share Co",
                    Cik = "0000000101",
                }
            );
            db.Add(
                new CommonStock
                {
                    Id = derivKindStockId,
                    Ticker = "DRVK",
                    Name = "Deriv Kind Co",
                    Cik = "0000000102",
                }
            );
            db.Add(
                new CommonStock
                {
                    Id = derivTitleStockId,
                    Ticker = "DRVT",
                    Name = "Deriv Title Co",
                    Cik = "0000000103",
                }
            );
            db.Add(
                new InsiderOwner
                {
                    Id = ownerId,
                    OwnerCik = "0005000010",
                    Name = "Derivative Holder",
                    IsTenPercentOwner = true,
                }
            );

            // A small genuine share transaction — the only one that should rank.
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = shareStockId,
                    InsiderOwnerId = ownerId,
                    TransactionDate = today.AddDays(-5),
                    FilingDate = today.AddDays(-3),
                    TransactionCode = TransactionCode.Sale,
                    AcquiredDisposed = AcquiredDisposed.Disposed,
                    Shares = 1_000,
                    PricePerShare = 100m,
                    SharesOwnedAfter = 0,
                    SecurityKind = InsiderSecurityKind.NonDerivative,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0005000010-25-000001",
                }
            );
            // Huge value via authoritative derivative kind — must be excluded.
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = derivKindStockId,
                    InsiderOwnerId = ownerId,
                    TransactionDate = today.AddDays(-5),
                    FilingDate = today.AddDays(-3),
                    TransactionCode = TransactionCode.Sale,
                    AcquiredDisposed = AcquiredDisposed.Disposed,
                    Shares = 1_000_000,
                    PricePerShare = 15_000_000m,
                    SharesOwnedAfter = 0,
                    SecurityKind = InsiderSecurityKind.Derivative,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = "0005000010-25-000002",
                }
            );
            // Huge value via title fallback (kind still Unknown) — must be excluded.
            db.Add(
                new InsiderTransaction
                {
                    CommonStockId = derivTitleStockId,
                    InsiderOwnerId = ownerId,
                    TransactionDate = today.AddDays(-5),
                    FilingDate = today.AddDays(-3),
                    TransactionCode = TransactionCode.Sale,
                    AcquiredDisposed = AcquiredDisposed.Disposed,
                    Shares = 1_000_000,
                    PricePerShare = 8_000_000m,
                    SharesOwnedAfter = 0,
                    SecurityKind = InsiderSecurityKind.Unknown,
                    SecurityTitle = "Pre-Funded Warrant (right to buy)",
                    AccessionNumber = "0005000010-25-000003",
                }
            );
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/insider-trading/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("SHRE");
        html.Should().NotContain("DRVK");
        html.Should().NotContain("DRVT");
    }

    // The same block sold by a PE sponsor is filed by every entity in its
    // beneficial-ownership chain — identical ticker/date/shares/price/code on
    // separate Form 4s. The boards must collapse those to a single row.
    [Fact]
    public async Task Dashboard_CollapsesDuplicateChainFilings()
    {
        var stockId = Guid.NewGuid();
        var chainOwners = new[] { "Chain Alpha LP", "Chain Bravo LP", "Chain Charlie LP" };
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            db.Add(
                new CommonStock
                {
                    Id = stockId,
                    Ticker = "DUPE",
                    Name = "Dupe Co",
                    Cik = "0000000201",
                }
            );
            for (var i = 0; i < chainOwners.Length; i++)
            {
                var ownerId = Guid.NewGuid();
                db.Add(
                    new InsiderOwner
                    {
                        Id = ownerId,
                        OwnerCik = $"000600000{i}",
                        Name = chainOwners[i],
                        IsTenPercentOwner = true,
                    }
                );
                // Identical block on each filing — same date/shares/price/code.
                db.Add(
                    new InsiderTransaction
                    {
                        CommonStockId = stockId,
                        InsiderOwnerId = ownerId,
                        TransactionDate = today.AddDays(-7),
                        FilingDate = today.AddDays(-5),
                        TransactionCode = TransactionCode.Sale,
                        AcquiredDisposed = AcquiredDisposed.Disposed,
                        Shares = 26_105_840,
                        PricePerShare = 41m,
                        SharesOwnedAfter = 0,
                        SecurityKind = InsiderSecurityKind.NonDerivative,
                        SecurityTitle = "Common Stock",
                        AccessionNumber = $"0006000000-25-00000{i}",
                    }
                );
            }
            await Task.CompletedTask;
        });

        var response = await _fixture.Client.GetAsync("/insider-trading/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        // The block survives as exactly one row, so only one of the three chain
        // entities is shown — regardless of which wins the value-sort tie.
        var ownersShown = chainOwners.Count(name => html.Contains(name));
        ownersShown.Should().Be(1);
    }
}
