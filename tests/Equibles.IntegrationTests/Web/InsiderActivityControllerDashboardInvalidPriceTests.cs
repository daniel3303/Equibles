using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.InsiderTrading.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Web;

[Collection(WebHostCollection.Name)]
public class InsiderActivityControllerDashboardInvalidPriceTests
{
    private readonly WebHostFixture _fixture;

    public InsiderActivityControllerDashboardInvalidPriceTests(WebHostFixture fixture) =>
        _fixture = fixture;

    // LoadTopRows filters `IsPriceValid != false`: it hides ONLY rows positively rejected
    // as implausible (false), while showing valid (true) and unevaluated (null). The
    // existing dashboard test seeds null-priced rows and asserts they appear, covering the
    // "show" half; this pins the "hide" half — an IsPriceValid == false row must NOT render,
    // while a valid sibling does. The price-invalid buy has a far larger dollar value, so
    // absent the filter it would top the board: its ticker's absence proves it was hidden,
    // and the valid ticker's presence proves the page isn't merely empty.
    [Fact]
    public async Task Dashboard_TransactionFlaggedPriceInvalid_IsHiddenWhileValidRowShows()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _fixture.ResetAndSeedAsync(async db =>
        {
            var owner = new InsiderOwner
            {
                Id = Guid.NewGuid(),
                OwnerCik = "0009990002",
                Name = "Dashboard Filter Owner",
                City = "New York",
                StateOrCountry = "NY",
                IsDirector = true,
            };
            db.Set<InsiderOwner>().Add(owner);

            var validStock = new CommonStock
            {
                Id = Guid.NewGuid(),
                Ticker = "ZVALID",
                Name = "Valid Price Co.",
            };
            var invalidStock = new CommonStock
            {
                Id = Guid.NewGuid(),
                Ticker = "ZINVAL",
                Name = "Fat Fingered Co.",
            };
            db.Set<CommonStock>().Add(validStock);
            db.Set<CommonStock>().Add(invalidStock);

            db.Set<InsiderTransaction>()
                .Add(MakeBuy(validStock, owner, "0009990002-25-000001", 1_000, 150.00m, true));
            db.Set<InsiderTransaction>()
                .Add(
                    MakeBuy(invalidStock, owner, "0009990002-25-000002", 9_999_999, 999_999m, false)
                );
            await Task.CompletedTask;

            InsiderTransaction MakeBuy(
                CommonStock s,
                InsiderOwner o,
                string accession,
                long shares,
                decimal price,
                bool isPriceValid
            ) =>
                new()
                {
                    Id = Guid.NewGuid(),
                    CommonStockId = s.Id,
                    InsiderOwnerId = o.Id,
                    FilingDate = today,
                    TransactionDate = today.AddDays(-2),
                    TransactionCode = TransactionCode.Purchase,
                    Shares = shares,
                    PricePerShare = price,
                    AcquiredDisposed = AcquiredDisposed.Acquired,
                    SharesOwnedAfter = 5_000,
                    OwnershipNature = OwnershipNature.Direct,
                    SecurityTitle = "Common Stock",
                    AccessionNumber = accession,
                    IsPriceValid = isPriceValid,
                };
        });

        var response = await _fixture.Client.GetAsync("/insider-trading/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("ZVALID");
        html.Should().NotContain("ZINVAL");
    }
}
